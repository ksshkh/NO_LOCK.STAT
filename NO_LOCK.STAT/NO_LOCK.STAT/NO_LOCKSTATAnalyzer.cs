﻿using System;
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

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return ImmutableArray.Create(Rule_MissingLock, Rule_DiffLockObjects); }
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(compilationStartContext =>
            {
                var allVariables = new ConcurrentDictionary<ISymbol, VariableStats>(SymbolEqualityComparer.Default);

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

                        if (variableInfo.numOfUnlocked > 0 && variableInfo.numOfLocked > 0)
                        {
                            foreach (var curStat in variableInfo.variableInfo)
                            {
                                if (curStat.lockObject == null)
                                {
                                    var diagnostic = Diagnostic.Create(
                                        Rule_MissingLock,
                                        curStat.location,
                                        variableName.Name,
                                        variableInfo.numOfLocked,
                                        variableInfo.numOfUnlocked
                                    );
                                    compilationEndContext.ReportDiagnostic(diagnostic);
                                }
                            }                            
                        }

                        string sampleObject = null;

                        foreach (var curStat in variableInfo.variableInfo)
                        {
                            if (curStat.lockObject != null && sampleObject == null)
                            {
                                sampleObject = curStat.lockObject;
                            }
                            else if (curStat.lockObject != sampleObject && curStat.lockObject != null)
                            {
                                var diagnostic = Diagnostic.Create(
                                    Rule_DiffLockObjects,
                                    curStat.location,
                                    variableName.Name,
                                    curStat.lockObject,
                                    sampleObject
                                );
                                compilationEndContext.ReportDiagnostic(diagnostic);
                            }
                        }

                    }
                });
            });
        }

        public class VariableVisitor : CSharpSyntaxWalker
        {
            private readonly SemanticModel _semanticModel;
            private readonly ConcurrentDictionary<ISymbol, VariableStats> _allVariables;

            public VariableVisitor(
                SemanticModel semanticModel,
                ConcurrentDictionary<ISymbol, VariableStats> allVariables)
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
                var stats = _allVariables.GetOrAdd(symbol, _ => new VariableStats());

                lock (stats) 
                {
                    stats.variableInfo.Add((lockObject, location));
                    if (lockObject != null) stats.numOfLocked++;
                    else stats.numOfUnlocked++;
                }
            }

            public class VariableStats
            {
                public int numOfUnlocked { get; set; }
                public int numOfLocked { get; set; }
                public List<(string lockObject, Location location)> variableInfo { get; }
                    = new List<(string, Location)>();
            }
        }
    }
}