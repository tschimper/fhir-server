﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.S3Storage.Features.Schema.Model;

namespace Microsoft.Health.Fhir.S3Storage.Features.Storage.TvpRowGeneration
{
    internal class UriSearchParameterRowGenerator : SearchParameterRowGenerator<UriSearchValue, V1.UriSearchParamTableTypeRow>
    {
        public UriSearchParameterRowGenerator(S3StorageFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, UriSearchValue searchValue, out V1.UriSearchParamTableTypeRow row)
        {
            row = new V1.UriSearchParamTableTypeRow(searchParamId, searchValue.Uri);
            return true;
        }
    }
}
