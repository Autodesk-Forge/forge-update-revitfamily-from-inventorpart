/////////////////////////////////////////////////////////////////////
// Copyright (c) Autodesk, Inc. All rights reserved
// Written by Forge Partner Development
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notice appears in all copies and
// that both that copyright notice and the limited warranty and
// restricted rights notice below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
/////////////////////////////////////////////////////////////////////

using Autodesk.Forge;
using Autodesk.Forge.Model;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Inventor2Revit.Controllers
{
    public class DesignAutomationController : ControllerBase
    {
        private IWebHostEnvironment _env;
        public DesignAutomationController(IWebHostEnvironment env)
        {
            _env = env;
        }

        [HttpPost]
        [Route("api/forge/callback/designautomation/inventor/{userId}/{projectId}/{versionId}")]
        public IActionResult OnReadyIpt2Sat(string userId, string projectId, string versionId, [FromBody]dynamic body)
        {
            // catch any errors, we don't want to return 500
            try
            {
                // your webhook should return immediately!
                // so can start a second thread (not good) or use a queueing system (e.g. hangfire)

                // starting a new thread is not an elegant idea, we don't have control if the operation actually complets...
                /*
                new System.Threading.Tasks.Task(async () =>
                  {
                      // your code here
                  }).Start();
                */

                // use Hangfire to schedule a job
                BackgroundJob.Schedule(() => StartRevit(userId, projectId, versionId, _env.WebRootPath), TimeSpan.FromSeconds(1));
            }
            catch { }

            // ALWAYS return ok (200)
            return Ok();
        }

        public async Task StartRevit(string userId, string projectId, string versionId, string contentRootPath)
        {
            /* string resultFilename = versionId + ".sat";

            if (!await client.DoesS3BucketExistAsync(Utils.S3BucketName)) return;
            Uri downloadFromS3 = new Uri(client.GeneratePreSignedURL(Utils.S3BucketName, resultFilename, DateTime.Now.AddMinutes(10), null)); */

            DesignAutomation4Revit daRevit = new DesignAutomation4Revit();
            await daRevit.StartUploadFamily(userId, projectId, versionId, contentRootPath);
        }

        [HttpPost]
        [Route("api/forge/callback/designautomation/revit/{userId}/{projectId}/{itemId}/{storageId}/{fileName}")]
        public IActionResult OnReadySat2Rvt(string userId, string projectId, string itemId, string storageId, string fileName, [FromBody]dynamic body)
        {
            // catch any errors, we don't want to return 500
            try
            {
                // your webhook should return immediately!
                // so can start a second thread (not good) or use a queueing system (e.g. hangfire)

                // starting a new thread is not an elegant idea, we don't have control if the operation actually complets...
                /*
                new System.Threading.Tasks.Task(async () =>
                  {
                      // your code here
                  }).Start();
                */

                // use Hangfire to schedule a job
                BackgroundJob.Schedule(() => PostProcessFile(userId, projectId, itemId.Base64Decode(), storageId.Base64Decode(), fileName.Base64Decode()), TimeSpan.FromSeconds(1));
            }
            catch { }

            // ALWAYS return ok (200)
            return Ok();
        }

        public async Task PostProcessFile(string userId, string projectId, string itemId, string storageId, string fileName)
        {
            Credentials credentials = await Credentials.FromDatabaseAsync(userId);

            VersionsApi versionsApis = new VersionsApi();
            versionsApis.Configuration.AccessToken = credentials.TokenInternal;
            CreateVersion newVersionData = new CreateVersion
            (
               new JsonApiVersionJsonapi(JsonApiVersionJsonapi.VersionEnum._0),
               new CreateVersionData
               (
                 CreateVersionData.TypeEnum.Versions,
                 new CreateStorageDataAttributes
                 (
                   fileName,
                   new BaseAttributesExtensionObject
                   (
                     "versions:autodesk.bim360:File",
                     "1.0",
                     new JsonApiLink(string.Empty),
                     null
                   )
                 ),
                 new CreateVersionDataRelationships
                 (
                    new CreateVersionDataRelationshipsItem
                    (
                      new CreateVersionDataRelationshipsItemData
                      (
                        CreateVersionDataRelationshipsItemData.TypeEnum.Items,
                        itemId
                      )
                    ),
                    new CreateItemRelationshipsStorage
                    (
                      new CreateItemRelationshipsStorageData
                      (
                        CreateItemRelationshipsStorageData.TypeEnum.Objects,
                        storageId
                      )
                    )
                 )
               )
            );
            dynamic newVersion = await versionsApis.PostVersionAsync(projectId, newVersionData);
        }
    }
}