using System;
using System.Collections.Concurrent;
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
using static NO_LOCK.STAT.NO_LOCKSTATAnalyzer.VariableVisitor;


namespace NO_LOCK.STAT
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class NO_LOCKSTATAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "NO_LOCKSTAT";

        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.MessageFormat), Resources.ResourceManager, typeof(Resources));      
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Usage";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);
        
        public const int threshold = 70;
        public const string NullKey = "##NULL_KEY##";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return ImmutableArray.Create(Rule); }
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(compilationStartContext =>
            {
                var allVariables = new ConcurrentDictionary<ISymbol, (ConcurrentDictionary<string, VariableStats>, int)>(SymbolEqualityComparer.Default);

                compilationStartContext.RegisterSyntaxTreeAction(treeContext =>
                {
                    var semanticModel = compilationStartContext.Compilation.GetSemanticModel(treeContext.Tree);
                    var root = treeContext.Tree.GetRoot();
                    var visitor = new VariableVisitor(semanticModel, allVariables);
                    visitor.Visit(root);
                });

                compilationStartContext.RegisterCompilationEndAction(compilationEndContext =>
                {
                    foreach (var curVariable in allVariables)
                    {
                        var variableName = curVariable.Key;
                        var variableInfo = curVariable.Value.Item1;
                        int totalNumOfUsage  = curVariable.Value.Item2;

                        foreach (var curLockObject in variableInfo)
                        {
                            if (curLockObject.Key == NullKey) continue;

                            int curThreshold = (int)((((double)curLockObject.Value.numOfUsage / (double)totalNumOfUsage) * 100));
                            if (curThreshold >= threshold)
                            {
                                foreach (var compareLockObject in variableInfo)
                                {
                                    if (compareLockObject.Key != curLockObject.Key)
                                    {
                                        foreach (var curLoc in compareLockObject.Value.variableLocation)
                                        {
                                            bool existsObject = curLockObject.Value.variableLocation.Contains(curLoc);
                                            if (existsObject) continue;

                                            string compareLockName = (compareLockObject.Key == NullKey) ? "no" : compareLockObject.Key;

                                            var diagnostic = Diagnostic.Create(
                                                Rule,
                                                curLoc,
                                                curLockObject.Key,
                                                variableName.Name,
                                                curThreshold,
                                                compareLockName
                                            );
                                            compilationEndContext.ReportDiagnostic(diagnostic);
                                        }
                                    }
                                }
                            }
                        }
                    }
                });
            });
        }

        public class VariableVisitor : CSharpSyntaxWalker
        {
            private readonly SemanticModel _semanticModel;
            private readonly ConcurrentDictionary<ISymbol, (ConcurrentDictionary<string, VariableStats>, int)> _allVariables;

            public VariableVisitor(
                SemanticModel semanticModel,
                ConcurrentDictionary<ISymbol, (ConcurrentDictionary<string, VariableStats>, int)> allVariables)
            {
                _semanticModel = semanticModel;
                _allVariables = allVariables;
            }

            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
                bool isLockObject = node.Parent is LockStatementSyntax;

                if (symbol == null || (symbol.Kind != SymbolKind.Field && symbol.Kind != SymbolKind.Property) || isLockObject)
                {
                    base.VisitIdentifierName(node);
                    return;
                }

                List<string> lockObjects = GetLockObjects(node);

                var variableEntry = _allVariables.GetOrAdd(symbol, _ => (new ConcurrentDictionary<string, VariableStats>(), 0));
                int count = variableEntry.Item2 + 1;
                _allVariables[symbol] = (variableEntry.Item1, count);

                if (lockObjects.Count == 0)
                {
                    UpdateVariableStats(symbol, NullKey, node.GetLocation());
                }
                else
                {
                    foreach (var curLockObject in lockObjects)
                    {
                        UpdateVariableStats(symbol, curLockObject, node.GetLocation());
                    }
                }

                base.VisitIdentifierName(node);
            }

            public List<string> GetLockObjects(SyntaxNode node)
            {
                var lockObjects = new List<string>();
                var parent = node.Parent;

                while (parent != null)
                {
                    if (parent is LockStatementSyntax lockStatement)
                    {
                        var symbolInfo = _semanticModel.GetSymbolInfo(lockStatement.Expression);
                        if (symbolInfo.Symbol != null)
                        {
                            lockObjects.Add(symbolInfo.Symbol.ToDisplayString());
                        }
                    }
                    parent = parent.Parent; 
                }

                return lockObjects; 
            }

            private void UpdateVariableStats(ISymbol symbol, string lockObject, Location location)
            {
                string lockKey = lockObject ?? NullKey;

                var variableEntry = _allVariables.GetOrAdd(symbol, _ => (new ConcurrentDictionary<string, VariableStats>(), 0));
                var innerDict = variableEntry.Item1;

                var stats = innerDict.GetOrAdd(lockKey, _ => new VariableStats());

                lock (stats)
                {
                    stats.variableLocation.Add(location);
                    stats.numOfUsage++;
                }
            }

            public class VariableStats
            {
                public int numOfUsage { get; set; }
                public List<Location> variableLocation { get; } = new List<Location>();
            }
        }
    }
}