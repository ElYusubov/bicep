// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Diagnostics;
using Bicep.Core.Emit;
using Bicep.Core.Parsing;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.Core.TypeSystem;
using Bicep.Core.Visitors;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace Bicep.Core.Analyzers.Linter.Rules
{
    public sealed class NoUnnecessaryDependsOnRule : LinterRuleBase
    {
        public new const string Code = "no-unnecessary-dependson";

        public NoUnnecessaryDependsOnRule() : base(
            code: Code,
            description: CoreResources.NoUnnecessaryDependsOnRuleDescription,
            docUri: new Uri($"https://aka.ms/bicep/linter/{Code}")
        )
        {
        }

        public override string FormatMessage(params object[] values)
            => string.Format(CoreResources.NoUnnecessaryDependsOnRuleMessage, values.First());

        public override IEnumerable<IDiagnostic> AnalyzeInternal(SemanticModel model)
        {
            ImmutableDictionary<DeclaredSymbol, ImmutableHashSet<ResourceDependency>> inferredDependenciesMap =
                ResourceDependencyVisitor.GetResourceDependencies(model, new ResourceDependencyVisitor.Options { IgnoreExplicitDependsOn = true });
            var visitor = new ResourceVisitor(this, inferredDependenciesMap, model);
            visitor.Visit(model.SourceFile.ProgramSyntax);
            return visitor.diagnostics;
        }

        private class ResourceVisitor : SyntaxVisitor
        {
            public List<IDiagnostic> diagnostics = new List<IDiagnostic>();

            private readonly NoUnnecessaryDependsOnRule parent;
            IImmutableDictionary<DeclaredSymbol, ImmutableHashSet<ResourceDependency>> inferredDependenciesMap;
            private readonly SemanticModel model;

            public ResourceVisitor(NoUnnecessaryDependsOnRule parent, IImmutableDictionary<DeclaredSymbol, ImmutableHashSet<ResourceDependency>> inferredDependenciesMap, SemanticModel model)
            {
                this.parent = parent;
                this.inferredDependenciesMap = inferredDependenciesMap;
                this.model = model;
            }

            public override void VisitResourceDeclarationSyntax(ResourceDeclarationSyntax syntax)
            {
                if (syntax.TryGetBody() is ObjectSyntax body)
                {
                    var dependsOnProperty = body.SafeGetPropertyByName(LanguageConstants.ResourceDependsOnPropertyName);
                    if (dependsOnProperty?.Value is ArraySyntax declaredDependencies)
                    {
                        if (model.GetSymbolInfo(syntax) is DeclaredSymbol thisResource)
                        {
                            // If this resource has no implicit dependencies, than all explicit dependsOn entries must be valid, so don't bother checking
                            if (inferredDependenciesMap.TryGetValue(thisResource, out ImmutableHashSet<ResourceDependency> inferredDependencies))
                            {
                                foreach (ArrayItemSyntax declaredDependency in declaredDependencies.Items)
                                {
                                    // Is this a simple reference to a resource collection?
                                    if (model.GetSymbolInfo(declaredDependency.Value) is ResourceSymbol referencedResouce)
                                    {
                                        if (referencedResouce.IsCollection)
                                        {
                                            // Ignore dependsOn entries pointing to a resource collection - dependency analyis would
                                            // be complex and user probably knows what they're doing.
                                            continue;
                                        }

                                        if (inferredDependencies.Any(d => d.Resource == referencedResouce))
                                        {
                                            this.diagnostics.Add(
                                                parent.CreateDiagnosticForSpan(
                                                    declaredDependency.Span,
                                                    referencedResouce.Name)); //asdff fixable
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                base.VisitResourceDeclarationSyntax(syntax);
            }
        }
    }
}
