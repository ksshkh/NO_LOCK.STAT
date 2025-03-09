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
        public const string DiagnosticIdMl = "NO_LOCKSTAT_ML";
        public const string DiagnosticIdDo = "NO_LOCKSTAT_DO";

        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));

        private static readonly LocalizableString MessageFormat_MissingLock = new LocalizableResourceString(nameof(Resources.MissingLock), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat_DiffLockObjects = new LocalizableResourceString(nameof(Resources.DiffLockObjects), Resources.ResourceManager, typeof(Resources));

        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Usage";

        private static readonly DiagnosticDescriptor Rule_MissingLock = new DiagnosticDescriptor(DiagnosticIdMl, Title, MessageFormat_MissingLock, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);
        private static readonly DiagnosticDescriptor Rule_DiffLockObjects = new DiagnosticDescriptor(DiagnosticIdDo, Title, MessageFormat_DiffLockObjects, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return ImmutableArray.Create(Rule_MissingLock, Rule_DiffLockObjects); }
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
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

            var visitor = new LockVisitor(context.SemanticModel, symbol);
            visitor.Visit(context.Node.SyntaxTree.GetRoot());

            var identifierNodes = visitor.GetIdentifiers();

            string LockObject = LockVisitor.GetLockObject(identifier);

            int num_of_locked = 0;
            int num_of_unlocked = 0;

            foreach (var (cur_identifier, CurLockObject) in identifierNodes)
            {
                if (CurLockObject != null)
                {
                    if (CurLockObject != LockObject && LockObject != null)
                    {
                        var diff_lock_objects = Diagnostic.Create(
                            descriptor: Rule_DiffLockObjects,
                            location: identifier.GetLocation(),
                            variableName, LockObject, CurLockObject);

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
                var no_lock_diagnostic = Diagnostic.Create(
                    descriptor: Rule_MissingLock,
                    location: identifier.GetLocation(),
                    variableName, num_of_locked, num_of_unlocked);

                context.ReportDiagnostic(no_lock_diagnostic);
            }
        }

        public class LockVisitor : CSharpSyntaxWalker
        {
            private readonly SemanticModel _semanticModel;
            private readonly ISymbol _targetSymbol;
            private readonly List<(IdentifierNameSyntax Identifier, string LockObject)> _identifiers = new List<(IdentifierNameSyntax, string)>();

            public LockVisitor(SemanticModel semanticModel, ISymbol targetSymbol)
            {
                _semanticModel = semanticModel;
                _targetSymbol = targetSymbol;
            }

            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
                if (symbol != null && symbol.Equals(_targetSymbol, SymbolEqualityComparer.Default))
                {
                    string lockObject = GetLockObject(node);
                    _identifiers.Add((node, lockObject));
                }

                base.VisitIdentifierName(node);
            }

            public static string GetLockObject(SyntaxNode node)
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

            public IEnumerable<(IdentifierNameSyntax Identifier, string LockObject)> GetIdentifiers()
            {
                return _identifiers;
            }
        }
    }
}