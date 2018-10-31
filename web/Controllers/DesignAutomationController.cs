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

using Amazon.S3;
using Autodesk.Forge;
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
        private IHostingEnvironment _env;
        public DesignAutomationController(IHostingEnvironment env)
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
                BackgroundJob.Schedule(() => StartRevit(userId, projectId, versionId, _env.ContentRootPath), TimeSpan.FromSeconds(1));
            }
            catch { }

            // ALWAYS return ok (200)
            return Ok();
        }

         public async Task StartRevit(string userId, string projectId, string versionId, string contentRootPath)
        {
            IAmazonS3 client = new AmazonS3Client(Amazon.RegionEndpoint.USWest2);

            string resultFilename = versionId + ".sat";

            // create AWS Bucket
            if (!await client.DoesS3BucketExistAsync(DesignAutomation4Inventor.S3BucketName)) return;
            Uri downloadFromS3 = new Uri(client.GeneratePreSignedURL(DesignAutomation4Inventor.S3BucketName, resultFilename, DateTime.Now.AddMinutes(10), null));


            // ToDo: is there a better way?
            string results = Path.Combine(contentRootPath, resultFilename);
            var keys = await client.GetAllObjectKeysAsync(DesignAutomation4Inventor.S3BucketName, null, null);
            if (!keys.Contains(resultFilename)) return; // file is not there
            await client.DownloadToFilePathAsync(DesignAutomation4Inventor.S3BucketName, resultFilename, results, null);
        }
    }
}