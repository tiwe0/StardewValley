using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace StardewValley.Logging
{
	/// <summary>A logger which writes to the console window in debug mode.</summary>
	internal class DefaultLogger : IGameLogger
	{
		/// <summary>The message builder used to format messages.</summary>
		private readonly StringBuilder MessageBuilder = new StringBuilder();

		/// <summary>The absolute path to the debug log file.</summary>
		private readonly string LogPath = Program.GetDebugLogPath();

		/// <summary>Whether to log messages to the console window.</summary>
		public bool ShouldWriteToConsole { get; }

		/// <summary>Whether to log messages to the debug log file.</summary>
		public bool ShouldWriteToLogFile { get; }

		/// <summary>Construct an instance.</summary>
		/// <param name="shouldWriteToConsole">Whether to log messages to the console window.</param>
		/// <param name="shouldWriteToLogFile">Whether to log messages to the debug log file.</param>
		public DefaultLogger(bool shouldWriteToConsole, bool shouldWriteToLogFile)
		{
			ShouldWriteToConsole = shouldWriteToConsole;
			ShouldWriteToLogFile = shouldWriteToLogFile;
			if (shouldWriteToLogFile)
			{
				File.WriteAllText(LogPath, "");
				IGameLogger log = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(22, 1);
				defaultInterpolatedStringHandler.AppendLiteral("Starting log file at ");
				defaultInterpolatedStringHandler.AppendFormatted(DateTime.Now, "yyyy-MM-dd HH:mm:ii");
				defaultInterpolatedStringHandler.AppendLiteral(".");
				log.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
			}
		}

		/// <inheritdoc />
		public void Verbose(string message)
		{
			LogImpl("Verbose", message);
		}

		/// <inheritdoc />
		public void Debug(string message)
		{
			LogImpl("Debug", message);
		}

		/// <inheritdoc />
		public void Info(string message)
		{
			LogImpl("Info", message);
		}

		/// <inheritdoc />
		public void Warn(string message)
		{
			LogImpl("Warn", message);
		}

		/// <inheritdoc />
		public void Error(string error, Exception exception)
		{
			LogImpl("Error", error, exception);
		}

		/// <summary>Log a message to the console and/or log file.</summary>
		/// <param name="level">The log level.</param>
		/// <param name="message">The message to log.</param>
		/// <param name="exception">The exception to logged, if applicable.</param>
		private void LogImpl(string level, string message, Exception exception = null)
		{
			bool logToConsole = ShouldWriteToConsole;
			bool logToFile = ShouldWriteToLogFile;
			if (!(logToConsole || logToFile))
			{
				return;
			}
			message = FormatLog(level, message, exception);
			if (logToConsole)
			{
				Console.WriteLine(message);
			}
			if (!logToFile)
			{
				return;
			}
			try
			{
				File.AppendAllText(LogPath, message);
			}
			catch (Exception ex)
			{
				if (logToConsole)
				{
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(28, 1);
					defaultInterpolatedStringHandler.AppendLiteral("Failed writing to log file:\n");
					defaultInterpolatedStringHandler.AppendFormatted(ex);
					Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
				}
			}
		}

		/// <summary>Format a log message with the date and level for display.</summary>
		/// <param name="level">The log level.</param>
		/// <param name="text">The message to log.</param>
		/// <param name="exception">The exception to logged, if applicable.</param>
		private string FormatLog(string level, string text, Exception exception = null)
		{
			StringBuilder message = MessageBuilder;
			try
			{
				int screenId = Game1.game1?.instanceId ?? 0;
				StringBuilder stringBuilder = message.Append('[');
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(1, 1, stringBuilder);
				handler.AppendFormatted(DateTime.Now, "HH:mm:ss");
				handler.AppendLiteral(" ");
				StringBuilder stringBuilder2 = stringBuilder.Append(ref handler).Append(level).Append(' ');
				object value;
				if (screenId != 0)
				{
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(6, 1);
					defaultInterpolatedStringHandler.AppendLiteral("screen");
					defaultInterpolatedStringHandler.AppendFormatted(screenId);
					value = defaultInterpolatedStringHandler.ToStringAndClear();
				}
				else
				{
					value = "game";
				}
				stringBuilder2.Append((string)value).Append("] ").Append(text)
					.AppendLine();
				if (exception != null)
				{
					message.Append(exception).AppendLine();
				}
				return message.ToString();
			}
			finally
			{
				message.Clear();
			}
		}
	}
}
