﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.S3Storage.Features.Schema.Model;

namespace Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class ChainAnchorQueryGenerator : NormalizedSearchParameterQueryGenerator
    {
        internal static readonly ChainAnchorQueryGenerator Instance = new ChainAnchorQueryGenerator();

        public override Table Table => null;
    }
}