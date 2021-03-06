﻿using MediaBrowser.Model.Logging;
using System;
using System.Globalization;

namespace MediaBrowser.Server.Implementations.HttpServer
{
    public static class LoggerUtils
    {
        /// <summary>
        /// Logs the response.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="statusCode">The status code.</param>
        /// <param name="url">The URL.</param>
        /// <param name="endPoint">The end point.</param>
        /// <param name="duration">The duration.</param>
        public static void LogResponse(ILogger logger, int statusCode, string url, string endPoint, TimeSpan duration)
        {
            logger.Info("HTTP Response {0} to {1}. Time: {2}ms. {3}", statusCode, endPoint, Convert.ToInt32(duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture), url);
        }
    }
}
