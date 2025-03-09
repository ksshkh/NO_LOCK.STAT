using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
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
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(compilationStartContext =>
            {
                compilationStartContext.RegisterSymbolStartAction(symbolStartContext =>
                {
                    AnalyzeSymbol(symbolStartContext);
                }, SymbolKind.Field);

                compilationStartContext.RegisterSymbolStartAction(symbolStartContext =>
                {
                    AnalyzeSymbol(symbolStartContext);
                }, SymbolKind.Property);
            });
        }

        private void AnalyzeSymbol(SymbolStartAnalysisContext context)
        {
            var symbol = context.Symbol;
            var variablesInfo = new List<(string lockObject, Location location)>();

            context.RegisterSyntaxNodeAction(nodeContext =>
            {
                var identifier = (IdentifierNameSyntax)nodeContext.Node;
                var currentSymbol = nodeContext.SemanticModel.GetSymbolInfo(identifier).Symbol;

                if (!SymbolEqualityComparer.Default.Equals(currentSymbol, symbol))
                    return;

                variablesInfo.Add((GetLockObject(identifier), identifier.GetLocation()));
            }, SyntaxKind.IdentifierName);

            context.RegisterSymbolEndAction(symbolEndContext =>
            {
                int numOfLocked = 0;
                int numOfUnlocked = 0;
                string sampleObject = null;

                foreach (var variableInfo in variablesInfo)
                {
                    if (variableInfo.lockObject == null)
                    {
                        numOfUnlocked++;
                    }
                    else
                    {
                        numOfLocked++;
                        if (sampleObject == null)
                        {
                            sampleObject = variableInfo.lockObject;
                        }
                    }
                }

                foreach (var variableInfo in variablesInfo)
                {
                    if (variableInfo.lockObject == null)
                    {
                        var noLockDiagnostic = Diagnostic.Create(
                            descriptor: Rule_MissingLock,
                            location: variableInfo.location,
                            symbol.Name, numOfLocked, numOfUnlocked);

                        symbolEndContext.ReportDiagnostic(noLockDiagnostic);
                    }
                    else if (variableInfo.lockObject != sampleObject)
                    {
                        var diffLockObjectsDiagnostic = Diagnostic.Create(
                            descriptor: Rule_DiffLockObjects,
                            location: variableInfo.location,
                            symbol.Name, sampleObject, variableInfo.lockObject);

                        symbolEndContext.ReportDiagnostic(diffLockObjectsDiagnostic);
                    }
                }
            });
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
    }
}