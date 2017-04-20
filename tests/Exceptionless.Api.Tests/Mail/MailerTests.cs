﻿using System;
using System.Threading.Tasks;
using Exceptionless.Core;
using Exceptionless.Core.Extensions;
using Exceptionless.Core.Jobs;
using Exceptionless.Core.Mail;
using Exceptionless.Core.Queues.Models;
using Exceptionless.Core.Models;
using Exceptionless.Core.Models.Data;
using Exceptionless.Core.Plugins.Formatting;
using Exceptionless.Tests.Utility;
using Foundatio.Logging;
using Foundatio.Metrics;
using Foundatio.Queues;
using Foundatio.Utility;
using Xunit;
using Xunit.Abstractions;

namespace Exceptionless.Api.Tests.Mail {
    public class MailerTests : TestBase {
        private readonly IMailer _mailer;

        public MailerTests(ITestOutputHelper output) : base(output) {
            _mailer = GetService<IMailer>();
            if (_mailer is NullMailer)
                _mailer = new Mailer(GetService<IQueue<MailMessage>>(), GetService<FormattingPluginManager>(), GetService<IMetricsClient>(), Log.CreateLogger<Mailer>());
        }

        [Fact]
        public Task SendEventNoticeSimpleErrorAsync() {
            var ex = GetException();
            return SendEventNoticeAsync(new PersistentEvent {
                Type = Event.KnownTypes.Error,
                Data = new Core.Models.DataDictionary {
                    {
                        Event.KnownDataKeys.SimpleError, new SimpleError {
                            Message = ex.Message,
                            Type = ex.GetType().FullName,
                            StackTrace = ex.StackTrace
                        }
                    }
                }
            });
        }

        [Fact]
        public Task SendEventNoticeErrorAsync() {
            return SendEventNoticeAsync(new PersistentEvent {
                Type = Event.KnownTypes.Error,
                Data = new Core.Models.DataDictionary {
                    {
                        Event.KnownDataKeys.Error, EventData.GenerateError()
                    }
                }
            });
        }

        [Fact]
        public Task SendEventNoticeNotFoundAsync() {
            return SendEventNoticeAsync(new PersistentEvent {
                Source = "[GET] /not-found?page=20",
                Type = Event.KnownTypes.NotFound
            });
        }

        [Fact]
        public Task SendEventNoticeFeatureAsync() {
            return SendEventNoticeAsync(new PersistentEvent {
                Source = "My Feature Usage",
                Value = 1,
                Type = Event.KnownTypes.FeatureUsage
            });
        }

        [Fact]
        public Task SendEventNoticeEmptyEventAsync() {
            return SendEventNoticeAsync(new PersistentEvent {
                Value = 1,
                Type = Event.KnownTypes.Log
            });
        }

        [Fact]
        public Task SendEventNoticeLogMessageAsync() {
            return SendEventNoticeAsync(new PersistentEvent {
                Message = "Only Message",
                Type = Event.KnownTypes.Log
            });
        }

        [Fact]
        public Task SendEventNoticeLogSourceAsync() {
            return SendEventNoticeAsync(new PersistentEvent {
                Source = "Only Source",
                Type = Event.KnownTypes.Log
            });
        }

        [Fact]
        public Task SendEventNoticeDefaultAsync() {
            return SendEventNoticeAsync(new PersistentEvent {
                Message = "Default Test Message",
                Source = "Default Test Source"
            });
        }

        private async Task SendEventNoticeAsync(PersistentEvent ev) {
            var user = UserData.GenerateSampleUser();
            var project = ProjectData.GenerateSampleProject();

            ev.Id = TestConstants.EventId;
            ev.OrganizationId = TestConstants.OrganizationId;
            ev.ProjectId = TestConstants.ProjectId;
            ev.StackId = TestConstants.StackId;

            await _mailer.SendEventNoticeAsync(user, ev, project, RandomData.GetBool(), RandomData.GetBool(), 1);
            await RunMailJobAsync();
        }

        [Fact]
        public async Task SendOrganizationAddedAsync() {
            var user = UserData.GenerateSampleUser();
            var organization = OrganizationData.GenerateSampleOrganization();

            await _mailer.SendOrganizationAddedAsync(user, organization, user);
            await RunMailJobAsync();
        }

        [Fact]
        public async Task SendOrganizationInviteAsync() {
            var user = UserData.GenerateSampleUser();
            var organization = OrganizationData.GenerateSampleOrganization();

            await _mailer.SendOrganizationInviteAsync(user, organization, new Invite {
                DateAdded = SystemClock.UtcNow,
                EmailAddress = Settings.Current.TestEmailAddress,
                Token = "1"
            });

            await RunMailJobAsync();

            if (GetService<IMailSender>() is InMemoryMailSender sender)
                Assert.Contains("Join Organization", sender.LastMessage.Body);
        }

        [Fact]
        public async Task SendOrganizationHourlyOverageNoticeAsync() {
            var user = UserData.GenerateSampleUser();
            var organization = OrganizationData.GenerateSampleOrganization();

            await _mailer.SendOrganizationNoticeAsync(user, organization, false, true);
            await RunMailJobAsync();
        }

        [Fact]
        public async Task SendOrganizationMonthlyOverageNoticeAsync() {
            var user = UserData.GenerateSampleUser();
            var organization = OrganizationData.GenerateSampleOrganization();

            await _mailer.SendOrganizationNoticeAsync(user, organization, true, false);
            await RunMailJobAsync();
        }

        [Fact]
        public async Task SendOrganizationPaymentFailedAsync() {
            var user = UserData.GenerateSampleUser();
            var organization = OrganizationData.GenerateSampleOrganization();

            await _mailer.SendOrganizationPaymentFailedAsync(user, organization);
            await RunMailJobAsync();
        }

        [Fact]
        public async Task SendProjectDailySummaryAsync() {
            var user = UserData.GenerateSampleUser();
            var project = ProjectData.GenerateSampleProject();

            await _mailer.SendProjectDailySummaryAsync(user, project, SystemClock.UtcNow.Date, true, 12, 1, 1, false);
            await RunMailJobAsync();
        }

        [Fact]
        public async Task SendProjectDailySummaryWithNoEventsAsync() {
            var user = UserData.GenerateSampleUser();
            var project = ProjectData.GenerateSampleProject();

            await _mailer.SendProjectDailySummaryAsync(user, project, SystemClock.UtcNow.Date, false, 0, 0, 0, false);
            await RunMailJobAsync();
        }

        [Fact]
        public async Task SendProjectDailySummaryWithFreeProjectAsync() {
            var user = UserData.GenerateSampleUser();
            var project = ProjectData.GenerateSampleProject();

            await _mailer.SendProjectDailySummaryAsync(user, project, SystemClock.UtcNow.Date, true, 12, 1, 1, true);
            await RunMailJobAsync();
        }

        [Fact]
        public async Task SendUserPasswordResetAsync() {
            var user = UserData.GenerateSampleUser();
            user.CreatePasswordResetToken();

            await _mailer.SendUserPasswordResetAsync(user);
            await RunMailJobAsync();
        }

        [Fact]
        public async Task SendUserEmailVerifyAsync() {
            var user = UserData.GenerateSampleUser();
            user.CreateVerifyEmailAddressToken();

            await _mailer.SendUserEmailVerifyAsync(user);
            await RunMailJobAsync();
        }

        private async Task RunMailJobAsync() {
            var job = GetService<MailMessageJob>();
            await job.RunAsync();

            var sender = GetService<IMailSender>() as InMemoryMailSender;
            if (sender == null)
                return;

            _logger.Trace($"To:       {sender.LastMessage.To}");
            _logger.Trace($"Subject: {sender.LastMessage.Subject}");
            _logger.Trace($"Body:\n{sender.LastMessage.Body}");
        }

        private Exception GetException() {
            void TestInner() {
                void TestInnerInner() {
                    throw new ApplicationException("Random Test Exception");
                }

                TestInnerInner();
            }

            try {
                TestInner();
            } catch (Exception ex) {
                return ex;
            }

            return null;
        }
    }
}