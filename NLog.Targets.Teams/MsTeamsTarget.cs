﻿using NLog.Config;
using NLog.Layouts;
using NLog.Targets.Teams;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NLog.Targets.MsTeams
{
    /// <summary>
    /// NLog Looging Target for MS Teams Incoming Webhook
    /// </summary>    
    [Target("MsTeams")]
    public sealed class MsTeamsTarget : AsyncTaskTarget
    {
        /// <summary>
        /// Ms Teams Incoming Webhook URL as string
        /// </summary>
        [RequiredParameter]
        public string Url { get; set; }

        /// <summary>
        /// fully qualified Name of the (custom) <see cref="IMessageCard"/> Implementation Class.<br/>
        /// if ommitted, the default Implementation will be used.
        /// </summary>        
        public string CardImpl { get; set; } = typeof(DefaultCard).FullName;

        /// <summary>
        /// fully qualified Assembly Name in which <see cref="CardImpl"/> is implemented.<br/>
        /// if ommitted, the Default Implementation in this assembly will be used.
        /// </summary>        
        public string CardAssembly { get; set; } = typeof(DefaultCard).Assembly.GetName().Name;

        /// <summary>
        /// Name of the Accplication<br/>
        /// Will be displayed as Title in the default card layout
        /// </summary>
        [RequiredParameter]
        public string ApplicationName { get; set; }

        /// <summary>
        /// Layout if the <see cref="ApplicationName"/> Parameter contains any vars or something like it
        /// </summary>
        private Layout _applicationNameLayout = null;

        /// <summary>
        /// Environment / Stage your Application runs in (eg. develop, stage, production)<br/>
        /// NOT a System Environment variable
        /// </summary>
        [RequiredParameter]
        public string Environment { get; set; }

        /// <summary>
        /// Layout if the <see cref="Environment"/> Parameter contains any vars or something like it
        /// </summary>
        private Layout _environmentLayout = null;

        /// <summary>
        /// Message Card reference
        /// </summary>
        private IMessageCard _messageCard = null;

        /// <summary>
        /// Message Card Implementation Reference
        /// </summary>
        private IMessageCard MessageCard
        {
            // laze initialization for the Card Implementation
            get
            {
                if (_messageCard == null)
                {
                    _messageCard = CreateMessageCard();
                }
                return _messageCard;
            }
        }

        /// <summary>
        /// Creates the Card Implementation from the Parameters <see cref="CardImpl"/> and <see cref="CardAssembly"/>
        /// </summary>
        /// <returns></returns>
        private IMessageCard CreateMessageCard()
        {
            string cardToInstantiate = $"{CardImpl}, {CardAssembly}";
            var cardType = Type.GetType(cardToInstantiate);

            return Activator.CreateInstance(cardType) as IMessageCard;
        }

        /// <summary>
        /// Construction
        /// </summary>        
        public MsTeamsTarget()
        {
            IncludeEventProperties = true; // Include LogEvent Properties by default                
        }

        /// <summary>
        /// <see cref="AsyncTaskTarget.InitializeTarget"/>
        /// </summary>
        protected override void InitializeTarget()
        {
            base.InitializeTarget();
            ResolveVariables();
        }

        /// <summary>
        /// Resolves the layout if the parameter contains one
        /// </summary>
        /// <param name="parameter"></param>
        /// <returns></returns>
        private Layout ResolveVar(string parameter)
        {
            SimpleLayout result = null;
            string pattern = @"\${var:(.*)}";
            Regex regex = new Regex(pattern);
            var match = regex.Match(parameter);
            if (!match.Success)
            {
                return result;
            }
            var key = match.Groups[1].Value;
            LoggingConfiguration.Variables.TryGetValue(key, out result);

            return result;
        }

        /// <summary>
        /// Resolves VAR: Parameters
        /// </summary>
        private void ResolveVariables()
        {
            _applicationNameLayout = ResolveVar(ApplicationName);
            _environmentLayout = ResolveVar(Environment);
        }

        /// <summary>
        /// <see cref="AsyncTaskTarget.WriteAsyncTask(LogEventInfo, CancellationToken)"/>
        /// </summary>
        /// <param name="logEvent"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected override async Task WriteAsyncTask(LogEventInfo logEvent, CancellationToken cancellationToken)
        {
            await CreateAndSendMessage(logEvent);
        }


        /// <summary>
        /// <see cref="AsyncTaskTarget.Write(Common.AsyncLogEventInfo)"/>
        /// </summary>
        /// <param name="logEvent"></param>
        protected override void Write(LogEventInfo logEvent)
        {
            CreateAndSendMessage(logEvent).GetAwaiter().GetResult();
        }


        /// <summary>
        /// internal implementation for async / sync writing
        /// </summary>
        /// <param name="logEvent"></param>
        /// <returns></returns>
        private async Task CreateAndSendMessage(LogEventInfo logEvent)
        {
            string logMessage = CreateMessage(logEvent);
            var response = await SendMessage(logMessage);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Rest Call Failed - {response.ReasonPhrase}");
            }
        }

        /// <summary>
        /// posts the message to the url
        /// </summary>
        /// <param name="logMessage"></param>
        private async Task<HttpResponseMessage> SendMessage(string logMessage)
        {
            var messageContent = new StringContent(logMessage);
            messageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            using (var httpClient = new HttpClient())
            {
                var targetUrl = new Uri(Url);
                return await httpClient.PostAsync(targetUrl, messageContent);
            }
        }

        /// <summary>
        /// Creates the Message string
        /// </summary>
        /// <param name="logEvent"></param>
        /// <returns></returns>
        private string CreateMessage(LogEventInfo logEvent)
        {
            var appname = ApplicationName;
            var env = Environment;
            // render layouts if necessary
            if (_applicationNameLayout != null)
            {
                appname = _applicationNameLayout.Render(logEvent);
            }
            if (_environmentLayout != null)
            {
                env = _environmentLayout.Render(logEvent);
            }

            // call the Card implementation
            return MessageCard.CreateMessage(logEvent, appname, env);
        }


    }
}
