﻿#region Copyright 2014 Exceptionless

// This program is free software: you can redistribute it and/or modify it 
// under the terms of the GNU Affero General Public License as published 
// by the Free Software Foundation, either version 3 of the License, or 
// (at your option) any later version.
// 
//     http://www.gnu.org/licenses/agpl-3.0.html

#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Web.Http;
using System.Web.Http.Hosting;
using System.Web.Http.Results;
using Exceptionless.Api.Controllers;
using Exceptionless.Api.Tests.Utility;
using Exceptionless.Core.Authorization;
using Exceptionless.Core.Extensions;
using Exceptionless.Core.Jobs;
using Exceptionless.Core.Models;
using Exceptionless.Core.Queues;
using Exceptionless.Tests.Utility;
using Microsoft.Owin;
using Xunit;

namespace Exceptionless.Api.Tests.Controllers {
    public class EventControllerTests : MongoTestHelper {
        private readonly EventController _eventController = IoC.GetInstance<EventController>();
        private readonly InMemoryQueue<EventPost> _eventQueue = IoC.GetInstance<IQueue<EventPost>>() as InMemoryQueue<EventPost>;

        public EventControllerTests() {
            ResetDatabase();
            AddSamples();
        }

        [Fact]
        public void CanPostString() {
            try {
                _eventController.Request = CreateRequestMessage(new ClaimsPrincipal(IdentityUtils.CreateUserIdentity(TestConstants.UserEmail, TestConstants.UserId, new[] { TestConstants.OrganizationId }, new[] { AuthorizationRoles.Client }, TestConstants.ProjectId)), false, false);
                var actionResult = _eventController.Post(Encoding.UTF8.GetBytes("simple string")).Result;
                Assert.IsType<OkResult>(actionResult);
                Assert.Equal(1, _eventQueue.Count);

                var processEventsJob = IoC.GetInstance<ProcessEventPostsJob>();
                var result = processEventsJob.Run();
                Assert.True(result.IsSuccess, result.Message);
                Assert.Equal(0, _eventQueue.Count);
                Assert.Equal(1, EventCount());
            } finally {
                RemoveAllEvents();
            }
        }

        [Fact]
        public void CanPostCompressedString() {
            try {
                _eventController.Request = CreateRequestMessage(new ClaimsPrincipal(IdentityUtils.CreateUserIdentity(TestConstants.UserEmail, TestConstants.UserId, new[] { TestConstants.OrganizationId }, new[] { AuthorizationRoles.Client }, TestConstants.ProjectId)), true, false);
                var actionResult = _eventController.Post(Encoding.UTF8.GetBytes("simple string").Compress()).Result;
                Assert.IsType<OkResult>(actionResult);
                Assert.Equal(1, _eventQueue.Count);

                var processEventsJob = IoC.GetInstance<ProcessEventPostsJob>();
                var result = processEventsJob.Run();
                Assert.True(result.IsSuccess, result.Message);
                Assert.Equal(0, _eventQueue.Count);
                Assert.Equal(1, EventCount());
            } finally {
                RemoveAllEvents();
            }
        }

        [Fact]
        public void CanPostSingleEvent() {
            _eventController.Request = CreateRequestMessage(new ClaimsPrincipal(IdentityUtils.CreateUserIdentity(TestConstants.UserEmail, TestConstants.UserId, new[] { TestConstants.OrganizationId }, new[] { AuthorizationRoles.Client }, TestConstants.ProjectId)), true, false);
            var actionResult = _eventController.Post(Encoding.UTF8.GetBytes("simple string").Compress()).Result;
            Assert.IsType<OkResult>(actionResult);
            Assert.Equal(1, _eventQueue.Count);

            var processEventsJob = IoC.GetInstance<ProcessEventPostsJob>();
            var result = processEventsJob.Run();
            Assert.Equal(0, _eventQueue.Count);
            RemoveAllEvents();
        }

        #region Helpers

        private HttpRequestMessage CreateRequestMessage(ClaimsPrincipal user, bool isCompressed, bool isJson, string charset = "utf-8") {
            var request = new HttpRequestMessage {
                Properties = {
                    { HttpPropertyKeys.HttpConfigurationKey, new HttpConfiguration() }
                }
            };

            var context = new OwinContext();
            context.Request.User = user;
            request.SetOwinContext(context);
            request.Content = new HttpMessageContent(new HttpRequestMessage(HttpMethod.Post, "/api/v1/event"));
            if (isCompressed)
                request.Content.Headers.ContentEncoding.Add("gzip");
            request.Content.Headers.ContentType.MediaType = isJson ? "application/json" : "text/plain";
            request.Content.Headers.ContentType.CharSet = charset;

            return request;
        }

        private static IEnumerable<object[]> Events {
            get {
                var result = new List<object[]>();
                foreach (var file in Directory.GetFiles(@"..\..\EventData\", "*.txt", SearchOption.AllDirectories).Where(f => !f.EndsWith(".expected.json")))
                    result.Add(new object[] { file });

                return result.ToArray();
            }
        }

        #endregion
    }
}