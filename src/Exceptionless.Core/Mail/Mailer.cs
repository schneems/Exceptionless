using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Exceptionless.Core.Extensions;
using Exceptionless.Core.Plugins.Formatting;
using Exceptionless.Core.Queues.Models;
using Exceptionless.Core.Models;
using Exceptionless.DateTimeExtensions;
using Foundatio.Logging;
using Foundatio.Metrics;
using Foundatio.Queues;
using Foundatio.Utility;
using HandlebarsDotNet;

namespace Exceptionless.Core.Mail {
    public class Mailer : IMailer {
        private readonly ConcurrentDictionary<string, Func<object, string>> _cachedTemplates = new ConcurrentDictionary<string, Func<object, string>>();
        private readonly IQueue<MailMessage> _queue;
        private readonly FormattingPluginManager _pluginManager;
        private readonly IMetricsClient _metrics;
        private readonly ILogger _logger;

        public Mailer(IQueue<MailMessage> queue, FormattingPluginManager pluginManager, IMetricsClient metrics, ILogger<Mailer> logger) {
            _queue = queue;
            _pluginManager = pluginManager;
            _metrics = metrics;
            _logger = logger;
        }

        public Task SendEventNoticeAsync(User user, PersistentEvent ev, Project project, bool isNew, bool isRegression, int totalOccurrences) {
            bool isCritical = ev.IsCritical();
            var data = _pluginManager.GetEventNotificationMailMessage(ev, isCritical, isNew, isRegression);
            if (data == null || data.Count == 0) {
                _logger.Warn("Unable to create event notification mail message for event \"{0}\". User: \"{1}\"", ev.Id, user.EmailAddress);
                return Task.CompletedTask;
            }

            data["BaseUrl"] = Settings.Current.BaseURL;
            data["IsCritical"] = isCritical;
            data["IsNew"] = isNew;
            data["IsRegression"] = isRegression;
            data["TotalOccurrences"] = totalOccurrences;
            data["ProjectName"] = project.Name;
            string subject = data["Subject"]?.ToString();
            if (String.IsNullOrEmpty(subject))
                data["Subject"] = subject = $"[{project.Name}] {ev.Message ?? ev.Source ?? "(Global)"}";

            const string template = "event-notice";
            return QueueMessageAsync(new MailMessage {
                To = user.EmailAddress,
                Subject = subject,
                Body = RenderTemplate(template, data)
            }, template);
        }

        public Task SendOrganizationAddedAsync(User sender, Organization organization, User user) {
            const string template = "organization-added";
            string subject = $"{sender.FullName} added you to the organization \"{organization.Name}\" on Exceptionless";
            var data = new Dictionary<string, object> {
                { "Subject", subject },
                { "BaseUrl", Settings.Current.BaseURL },
                { "OrganizationId", organization.Id },
                { "OrganizationName", organization.Name }
            };

            return QueueMessageAsync(new MailMessage {
                To = user.EmailAddress,
                Subject = subject,
                Body = RenderTemplate(template, data)
            }, template);
        }

        public Task SendOrganizationInviteAsync(User sender, Organization organization, Invite invite) {
            const string template = "organization-invited";
            string subject = $"{sender.FullName} invited you to join the organization \"{organization.Name}\" on Exceptionless";
            var data = new Dictionary<string, object> {
                { "Subject", subject },
                { "BaseUrl", Settings.Current.BaseURL },
                { "InviteToken", invite.Token }
            };

            return QueueMessageAsync(new MailMessage {
                To = invite.EmailAddress,
                Subject = subject,
                Body = RenderTemplate(template, data)
            }, template);
        }

        public Task SendOrganizationNoticeAsync(User user, Organization organization, bool isOverMonthlyLimit, bool isOverHourlyLimit) {
            const string template = "organization-notice";
            string subject = isOverMonthlyLimit
                    ? $"[{organization.Name}] Monthly plan limit exceeded."
                    : $"[{organization.Name}] Events are currently being throttled.";

            var data = new Dictionary<string, object> {
                { "Subject", subject },
                { "BaseUrl", Settings.Current.BaseURL },
                { "OrganizationId", organization.Id },
                { "OrganizationName", organization.Name },
                { "IsOverMonthlyLimit", isOverMonthlyLimit },
                { "IsOverHourlyLimit", isOverHourlyLimit },
                { "ThrottledUntil", SystemClock.UtcNow.StartOfHour().AddHours(1).ToShortTimeString() }
            };

            return QueueMessageAsync(new MailMessage {
                To = user.EmailAddress,
                Subject = subject,
                Body = RenderTemplate(template, data)
            }, template);
        }

        public Task SendOrganizationPaymentFailedAsync(User owner, Organization organization) {
            const string template = "organization-payment-failed";
            string subject = $"[{organization.Name}] Payment failed! Update billing information to avoid service interuption!";
            var data = new Dictionary<string, object> {
                { "Subject", subject },
                { "BaseUrl", Settings.Current.BaseURL },
                { "OrganizationId", organization.Id },
                { "OrganizationName", organization.Name }
            };

            return QueueMessageAsync(new MailMessage {
                To = owner.EmailAddress,
                Subject = subject,
                Body = RenderTemplate(template, data)
            }, template);
        }

        public Task SendProjectDailySummaryAsync(User user, Project project, DateTime startDate, bool hasSubmittedEvents, long total, double uniqueTotal, double newTotal, bool isFreePlan) {
            const string template = "project-daily-summary";
            string subject = $"[{project.Name}] Summary for {startDate.ToShortDateString()}";
            var data = new Dictionary<string, object> {
                { "Subject", subject },
                { "BaseUrl", Settings.Current.BaseURL },
                { "OrganizationId", project.OrganizationId },
                { "ProjectId", project.Id },
                { "ProjectName", project.Name },
                { "StartDate", startDate.ToLongDateString() },
                { "HasSubmittedEvents", hasSubmittedEvents },
                { "Total", total },
                { "UniqueTotal", uniqueTotal },
                { "NewTotal", newTotal },
                { "IsFreePlan", isFreePlan }
            };

            return QueueMessageAsync(new MailMessage {
                To = user.EmailAddress,
                Subject = subject,
                Body = RenderTemplate(template, data)
            }, template);
        }

        public Task SendUserEmailVerifyAsync(User user) {
            if (String.IsNullOrEmpty(user?.VerifyEmailAddressToken))
                return Task.CompletedTask;

            const string template = "user-email-verify";
            const string subject = "Exceptionless Account Confirmation";
            var data = new Dictionary<string, object> {
                { "Subject", subject },
                { "BaseUrl", Settings.Current.BaseURL },
                { "UserFullName", user.FullName },
                { "UserVerifyEmailAddressToken", user.VerifyEmailAddressToken }
            };

            return QueueMessageAsync(new MailMessage {
                To = user.EmailAddress,
                Subject = subject,
                Body = RenderTemplate(template, data)
            }, template);
        }

        public Task SendUserPasswordResetAsync(User user) {
            if (String.IsNullOrEmpty(user?.PasswordResetToken))
                return Task.CompletedTask;

            const string template = "user-password-reset";
            const string subject = "Exceptionless Password Reset";
            var data = new Dictionary<string, object> {
                { "Subject", subject },
                { "BaseUrl", Settings.Current.BaseURL },
                { "UserFullName", user.FullName },
                { "UserPasswordResetToken", user.PasswordResetToken }
            };

            return QueueMessageAsync(new MailMessage {
                To = user.EmailAddress,
                Subject = subject,
                Body = RenderTemplate(template, data)
            }, template);
        }

        private string RenderTemplate(string name, IDictionary<string, object> data) {
            var template = GetCompiledTemplate(name);
            return template(data);
        }

        private Func<object, string> GetCompiledTemplate(string name) {
            return _cachedTemplates.GetOrAdd(name, templateName => {
                var assembly = typeof(Mailer).Assembly;
                string resourceName = $"Exceptionless.Core.Mail.Templates.{templateName}.html";

                using (var stream = assembly.GetManifestResourceStream(resourceName)) {
                    using (var reader = new StreamReader(stream)) {
                        string template = reader.ReadToEnd();
                        return Handlebars.Compile(template);
                    }
                }
            });
        }

        private Task QueueMessageAsync(MailMessage message, string metricsName) {
            CleanAddresses(message);
            return Task.WhenAll(
                _metrics.CounterAsync($"mailer.{metricsName}"),
                _queue.EnqueueAsync(message)
            );
        }

        private static void CleanAddresses(MailMessage message) {
            if (Settings.Current.WebsiteMode == WebsiteMode.Production)
                return;

            if (Settings.Current.AllowedOutboundAddresses.Contains(message.To.ToLowerInvariant()))
                return;

            message.Subject = $"[{message.To}] {message.Subject}".StripInvisible();
            message.To = Settings.Current.TestEmailAddress;
        }
    }
}