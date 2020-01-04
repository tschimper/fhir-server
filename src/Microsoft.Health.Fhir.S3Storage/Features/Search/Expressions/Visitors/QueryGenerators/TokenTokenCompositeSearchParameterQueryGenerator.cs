// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.S3Storage.Features.Schema.Model;

namespace Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class TokenTokenCompositeSearchParameterQueryGenerator : CompositeSearchParameterQueryGenerator
    {
        public static readonly TokenTokenCompositeSearchParameterQueryGenerator Instance = new TokenTokenCompositeSearchParameterQueryGenerator();

        public TokenTokenCompositeSearchParameterQueryGenerator()
            : base(TokenSearchParameterQueryGenerator.Instance, TokenSearchParameterQueryGenerator.Instance)
        {
        }

        public override Table Table => V1.TokenTokenCompositeSearchParam;
    }
}