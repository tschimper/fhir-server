// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.S3Storage.Api.Controllers;
using Microsoft.Health.Fhir.S3Storage.Api.Features.Routing;
using Microsoft.Health.Fhir.S3Storage.Features.Schema;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.S3Storage.Api.UnitTests.Controllers
{
    public class S3SchemaControllerTests
    {
        private readonly S3SchemaController _schemaController;

        public S3SchemaControllerTests()
        {
            var schemaInformation = new S3SchemaInformation();
            var urlResolver = Substitute.For<IUrlResolver>();
            urlResolver.ResolveRouteNameUrl(RouteNames.Script, Arg.Any<IDictionary<string, object>>()).Returns(new Uri("https://localhost/script"));
            _schemaController = new S3SchemaController(schemaInformation, urlResolver, NullLogger<S3SchemaController>.Instance);
        }

        [Fact]
        public void GivenAScriptRequest_WhenSchemaIdFound_ThenReturnScriptSuccess()
        {
            ActionResult result = _schemaController.SqlScript(1);
            string script = result.ToString();
            Assert.NotNull(script);
        }

        [Fact]
        public void GivenAnAvailableVersionsRequest_WhenCurrentVersionIsNull_ThenAllVersionsReturned()
        {
            ActionResult result = _schemaController.AvailableVersions();

            var jsonResult = result as JsonResult;
            Assert.NotNull(jsonResult);

            var jArrayResult = JArray.FromObject(jsonResult.Value);
            Assert.Equal(Enum.GetNames(typeof(S3SchemaVersion)).Length, jArrayResult.Count);

            JToken firstResult = jArrayResult.First;
            Assert.Equal(1, firstResult["id"]);
            Assert.Equal("https://localhost/script", firstResult["script"]);
        }

        [Fact]
        public void GivenACurrentVersiontRequest_WhenNotImplemented_ThenNotImplementedShouldBeThrown()
        {
            Assert.Throws<NotImplementedException>(() => _schemaController.CurrentVersion());
        }

        [Fact]
        public void GivenACompatibilityRequest_WhenNotImplemented_ThenNotImplementedShouldBeThrown()
        {
            Assert.Throws<NotImplementedException>(() => _schemaController.Compatibility());
        }
    }
}
