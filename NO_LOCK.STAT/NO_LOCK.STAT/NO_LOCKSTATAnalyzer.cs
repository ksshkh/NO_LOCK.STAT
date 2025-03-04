using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.SymbolStore;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using System.Xml.Xsl;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NO_LOCK.STAT
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class NO_LOCKSTATAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "NO_LOCKSTAT";

        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Usage";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.IdentifierName);
        }

        private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
        {
            var identifier = (IdentifierNameSyntax)context.Node;
            var symbol = context.SemanticModel.GetSymbolInfo(identifier).Symbol;
            if (symbol == null || (symbol.Kind != SymbolKind.Field && symbol.Kind != SymbolKind.Property))
            {
                return; 
            }

            string variableName = symbol.Name;
            var root = context.Node.SyntaxTree.GetRoot();
            var identifierNodes = root.DescendantNodes()
                                  .OfType<IdentifierNameSyntax>()
                                  .Where(id => id.Identifier.ValueText == variableName);

            string LockObject = GetLockObject(identifier);

            int num_of_locked = 0;
            int num_of_unlocked = 0;

            foreach (var cur_identifier in identifierNodes)
            {
                string CurLockObject = GetLockObject(cur_identifier);
                if (CurLockObject != null)
                {
                    if (CurLockObject != LockObject && LockObject != null)
                    {
                        string message1 = string.Format(Resources.DiffLockObjects, variableName, LockObject, CurLockObject);

                        var diff_lock_objects = Diagnostic.Create(
                            descriptor: Rule,
                            location: identifier.GetLocation(),
                            messageArgs: message1);

                        context.ReportDiagnostic(diff_lock_objects);
                    }
                    num_of_locked++;
                }
                else
                {
                    num_of_unlocked++;
                }
            }

            if (LockObject == null)
            {                            
                string message = string.Format(Resources.VariableMessage, variableName, num_of_locked, num_of_unlocked);
                    
                var no_lock_diagnostic = Diagnostic.Create(
                    descriptor: Rule,
                    location: identifier.GetLocation(),
                    messageArgs: message);

                context.ReportDiagnostic(no_lock_diagnostic);
                
            }
        }

        private static string GetLockObject(SyntaxNode node)
        {
            var parent = node.Parent;
            while (parent != null)
            {
                if (parent is LockStatementSyntax lockStatement)
                {
                    return lockStatement.Expression.ToString();
                }
                parent = parent.Parent;
            }
            return null; 
        }

    }
}
