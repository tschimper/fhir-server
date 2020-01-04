﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Reflection;

namespace Microsoft.Health.Fhir.S3Storage.Features.Schema
{
    public static class S3ScriptProvider
    {
        public static string GetMigrationScript(int version)
        {
            string resourceName = $"{typeof(S3ScriptProvider).Namespace}.Migrations.{version}.sql";
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException(Resources.ScriptNotFound);
                }

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        public static byte[] GetMigrationScriptAsBytes(int version)
        {
            string resourceName = $"{typeof(S3ScriptProvider).Namespace}.Migrations.{version}.sql";
            using (Stream filestream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (filestream == null)
                {
                    throw new FileNotFoundException(Resources.ScriptNotFound);
                }

                byte[] scriptBytes = new byte[filestream.Length];
                filestream.Read(scriptBytes, 0, scriptBytes.Length);
                return scriptBytes;
            }
        }
    }
}
