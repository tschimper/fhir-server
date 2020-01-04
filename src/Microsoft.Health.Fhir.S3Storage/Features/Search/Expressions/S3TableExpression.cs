// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors.QueryGenerators;

namespace Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions
{
    /// <summary>
    /// An expression over a search param or compartment table.
    /// </summary>
    internal class S3TableExpression : Expression
    {
        public S3TableExpression(
            NormalizedSearchParameterQueryGenerator searchParameterQueryGenerator,
            Expression normalizedPredicate,
            Expression denormalizedPredicate,
            S3TableExpressionKind kind,
            int chainLevel = 0)
        {
            switch (normalizedPredicate)
            {
                case SearchParameterExpressionBase _:
                case CompartmentSearchExpression _:
                case ChainedExpression _:
                case IncludeExpression _:
                case null:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(normalizedPredicate));
            }

            SearchParameterQueryGenerator = searchParameterQueryGenerator;
            NormalizedPredicate = normalizedPredicate;
            DenormalizedPredicate = denormalizedPredicate;
            Kind = kind;
            ChainLevel = chainLevel;
        }

        public S3TableExpressionKind Kind { get; }

        public int ChainLevel { get; }

        public NormalizedSearchParameterQueryGenerator SearchParameterQueryGenerator { get; }

        public Expression NormalizedPredicate { get; }

        public Expression DenormalizedPredicate { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            return AcceptVisitor((IS3SqlExpressionVisitor<TContext, TOutput>)visitor, context);
        }

        public TOutput AcceptVisitor<TContext, TOutput>(IS3SqlExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            return visitor.VisitTable(this, context);
        }

        public override string ToString()
        {
            return $"(Table {Kind} {(ChainLevel == 0 ? null : $"ChainLevel:{ChainLevel} ")}{SearchParameterQueryGenerator?.Table} Normalized:{NormalizedPredicate} Denormalized:{DenormalizedPredicate})";
        }
    }
}
