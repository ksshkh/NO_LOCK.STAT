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
        public const string DiagnosticIdMl = "NO_LOCKSTAT_ML";
        public const string DiagnosticIdDo = "NO_LOCKSTAT_DO";

        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));

        private static readonly LocalizableString MessageFormat_MissingLock = new LocalizableResourceString(nameof(Resources.MissingLock), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat_DiffLockObjects = new LocalizableResourceString(nameof(Resources.DiffLockObjects), Resources.ResourceManager, typeof(Resources));

        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Usage";

        private static readonly DiagnosticDescriptor Rule_MissingLock = new DiagnosticDescriptor(DiagnosticIdMl, Title, MessageFormat_MissingLock, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);
        private static readonly DiagnosticDescriptor Rule_DiffLockObjects = new DiagnosticDescriptor(DiagnosticIdDo, Title, MessageFormat_DiffLockObjects, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public const int threshold = 70;
        public const string NullKey = "##NULL_KEY##";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return ImmutableArray.Create(Rule_MissingLock, Rule_DiffLockObjects); }
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(compilationStartContext =>
            {
                var allVariables = new ConcurrentDictionary<ISymbol, ConcurrentDictionary<string, VariableStats>>(SymbolEqualityComparer.Default);

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
                        var variableInfo = curVariable.Value;

                        int totalNumOfUsage  = 0;
                        int numOfUnlocked    = 0;
                        int maxLock          = 0;
                        string maxLockObject = null;

                        foreach (var curLockObject in variableInfo)
                        {
                            if (curLockObject.Key == NullKey)
                            {
                                numOfUnlocked = curLockObject.Value.numOfUsage;
                            }                            
                            else if (maxLock < curLockObject.Value.numOfUsage)
                            {
                                maxLock = curLockObject.Value.numOfUsage;
                                maxLockObject = curLockObject.Key;
                            }

                            totalNumOfUsage += curLockObject.Value.numOfUsage;
                        }

                        if (totalNumOfUsage == 0) return;

                        int curThreshold = (int)((((double)maxLock / (double)totalNumOfUsage) * 100));
                        if (curThreshold >= threshold)
                        {
                            foreach (var curLockObject in variableInfo)
                            {
                                if (curLockObject.Key == NullKey)
                                {
                                    foreach (var curLoc in curLockObject.Value.variableLocation)
                                    {
                                        var diagnostic = Diagnostic.Create(
                                            Rule_MissingLock,
                                            curLoc,
                                            variableName.Name,
                                            curThreshold,
                                            maxLock,
                                            totalNumOfUsage - maxLock
                                        );
                                        compilationEndContext.ReportDiagnostic(diagnostic);
                                    }
                                }
                                else if (curLockObject.Key != maxLockObject)
                                {
                                    foreach (var curLoc in curLockObject.Value.variableLocation)
                                    {
                                        var diagnostic = Diagnostic.Create(
                                            Rule_DiffLockObjects,
                                            curLoc,
                                            variableName.Name,
                                            curThreshold,
                                            maxLockObject,
                                            curLockObject.Key
                                        );
                                        compilationEndContext.ReportDiagnostic(diagnostic);
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
            private readonly ConcurrentDictionary<ISymbol, ConcurrentDictionary<string, VariableStats>> _allVariables;

            public VariableVisitor(
                SemanticModel semanticModel,
                ConcurrentDictionary<ISymbol, ConcurrentDictionary<string, VariableStats>> allVariables)
            {
                _semanticModel = semanticModel;
                _allVariables = allVariables;
            }

            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
                if (symbol == null || (symbol.Kind != SymbolKind.Field && symbol.Kind != SymbolKind.Property))
                {
                    base.VisitIdentifierName(node);
                    return;
                }

                string lockObject = GetLockObject(node);
                UpdateVariableStats(symbol, lockObject, node.GetLocation());

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

            private void UpdateVariableStats(ISymbol symbol, string lockObject, Location location)
            {
                string lockKey = lockObject ?? NullKey;

                var innerDict = _allVariables.GetOrAdd(symbol,
                    _ => new ConcurrentDictionary<string, VariableStats>());

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