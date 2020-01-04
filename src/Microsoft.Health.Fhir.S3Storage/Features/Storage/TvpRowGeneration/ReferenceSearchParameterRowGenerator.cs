﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.S3Storage.Features.Schema.Model;

namespace Microsoft.Health.Fhir.S3Storage.Features.Storage.TvpRowGeneration
{
    internal class ReferenceSearchParameterRowGenerator : SearchParameterRowGenerator<ReferenceSearchValue, V1.ReferenceSearchParamTableTypeRow>
    {
        public ReferenceSearchParameterRowGenerator(S3StorageFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, ReferenceSearchValue searchValue, out V1.ReferenceSearchParamTableTypeRow row)
        {
            row = new V1.ReferenceSearchParamTableTypeRow(
                searchParamId,
                searchValue.BaseUri?.ToString(),
                Model.GetResourceTypeId(searchValue.ResourceType.ToString()),
                searchValue.ResourceId,
                ReferenceResourceVersion: null);

            return true;
        }
    }
}
