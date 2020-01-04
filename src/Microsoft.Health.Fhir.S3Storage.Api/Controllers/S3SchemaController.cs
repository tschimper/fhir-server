// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.S3Storage.Api.Features.Filters;
using Microsoft.Health.Fhir.S3Storage.Api.Features.Routing;
using Microsoft.Health.Fhir.S3Storage.Features.Schema;

namespace Microsoft.Health.Fhir.S3Storage.Api.Controllers
{
    [S3HttpExceptionFilter]
    [Route(KnownRoutes.SchemaRoot)]
    public class S3SchemaController : Controller
    {
        private readonly S3SchemaInformation _schemaInformation;
        private readonly IUrlResolver _urlResolver;
        private readonly ILogger<S3SchemaController> _logger;

        public S3SchemaController(S3SchemaInformation schemaInformation, IUrlResolver urlResolver, ILogger<S3SchemaController> logger)
        {
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _schemaInformation = schemaInformation;
            _urlResolver = urlResolver;
            _logger = logger;
        }

        [HttpGet]
        [AllowAnonymous]
        [Route(KnownRoutes.Versions)]
        public ActionResult AvailableVersions()
        {
            _logger.LogInformation("Attempting to get available schemas");

            var availableSchemas = new List<object>();
            var currentVersion = _schemaInformation.Current ?? 0;
            foreach (var version in Enum.GetValues(typeof(S3SchemaVersion)).Cast<S3SchemaVersion>().Where(sv => sv >= currentVersion))
            {
                var routeValues = new Dictionary<string, object> { { "id", (int)version } };
                Uri scriptUri = _urlResolver.ResolveRouteNameUrl(RouteNames.Script, routeValues);
                availableSchemas.Add(new { id = version, script = scriptUri });
            }

            return new JsonResult(availableSchemas);
        }

        [HttpGet]
        [AllowAnonymous]
        [Route(KnownRoutes.Current)]
        public ActionResult CurrentVersion()
        {
            _logger.LogInformation("Attempting to get current schemas");

            throw new NotImplementedException(Resources.CurrentVersionNotImplemented);
        }

        [HttpGet]
        [AllowAnonymous]
        [Route(KnownRoutes.Script, Name = RouteNames.Script)]
        public FileContentResult SqlScript(int id)
        {
            _logger.LogInformation($"Attempting to get script for schema version: {id}");
            string fileName = $"{id}.sql";
            return File(S3ScriptProvider.GetMigrationScriptAsBytes(id), "application/json", fileName);
        }

        [HttpGet]
        [AllowAnonymous]
        [Route(KnownRoutes.Compatibility)]
        public ActionResult Compatibility()
        {
            _logger.LogInformation("Attempting to get compatibility");

            throw new NotImplementedException(Resources.CompatibilityNotImplemented);
        }
    }
}
