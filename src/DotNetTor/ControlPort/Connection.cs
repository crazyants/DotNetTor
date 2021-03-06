﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DotNetTor.ControlPort
{
	/// <summary>
	/// A class containing methods for interacting with a control connection for a tor application.
	/// </summary>
	internal sealed class Connection : IDisposable
	{
		private readonly static string EOL = "\r\n";
		private volatile bool disposed;
		private StreamReader reader;
		private Socket socket;
		private NetworkStream stream;
		private string _address;
		private int _controlPort;
		public string Address
		{
			get
			{
				return _address;
			}

			set
			{
				_address = value;
			}
		}
		public int ControlPort
		{
			get
			{
				return _controlPort;
			}

			set
			{
				_controlPort = value;
			}
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Connection"/> class.
		/// </summary>
		/// <param name="client">The client hosting the control connection.</param>
		public Connection(string address, int controlPort)
		{
			disposed = false;
			reader = null;
			socket = null;
			stream = null;
			_address = address;
			_controlPort = controlPort;
		}

		/// <summary>
		/// Finalizes an instance of the <see cref="Connection"/> class.
		/// </summary>
		~Connection()
		{
			Dispose(false);
		}

		#region System.IDisposable

		/// <summary>
		/// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// Releases unmanaged and - optionally - managed resources.
		/// </summary>
		/// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
		private void Dispose(bool disposing)
		{
			if (disposed)
				return;

			if (disposing)
			{
				if (reader != null)
				{
					reader.Dispose();
					reader = null;
				}

				if (stream != null)
				{
					stream.Dispose();
					stream = null;
				}

				if (socket != null)
				{
					if (socket.Connected)
						socket.Shutdown(SocketShutdown.Both);

					socket.Dispose();
					socket = null;
				}

				disposed = true;
			}
		}

		#endregion

		/// <summary>
		/// Authenticates the connection by sending the password to the control port.
		/// </summary>
		/// <param name="password">The password used for authentication.</param>
		/// <returns><c>true</c> if the authentication succeeds; otherwise, <c>false</c>.</returns>
		public bool Authenticate(string password)
		{
			if (disposed)
				throw new ObjectDisposedException("this");

			if (password == null)
				password = "";

			if (Write("authenticate \"{0}\"", password))
			{
				ConnectionResponse response = Read();

				if (response.Success)
					return true;
			}

			return false;
		}

		/// <summary>
		/// Connects to the control port hosted by the client.
		/// </summary>
		/// <returns><c>true</c> if the connection succeeds; otherwise, <c>false</c>.</returns>
		public bool Connect()
		{
			if (disposed)
				throw new ObjectDisposedException("this");

			try
			{
				socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				socket.Connect(Address, ControlPort);

				stream = new NetworkStream(socket, false);
				stream.ReadTimeout = 2000;

				reader = new StreamReader(stream);

				return true;
			}
			catch
			{
				if (reader != null)
				{
					reader.Dispose();
					reader = null;
				}

				if (stream != null)
				{
					stream.Dispose();
					stream = null;
				}

				if (socket != null)
				{
					if (socket.Connected)
						socket.Shutdown(SocketShutdown.Both);

					socket.Dispose();
					socket = null;
				}

				return false;
			}
		}

		/// <summary>
		/// Reads a response buffer from the control connection. This method is blocking with a receive timeout of 500ms.
		/// </summary>
		/// <returns>A <see cref="ConnectionResponse"/> containing the response information.</returns>
		public ConnectionResponse Read()
		{
			if (disposed)
				throw new ObjectDisposedException("this");
			if (socket == null || stream == null || reader == null)
				return new ConnectionResponse(StatusCode.Unknown);

			try
			{
				string line = reader.ReadLine();

				if (line == null)
					return new ConnectionResponse(StatusCode.Unknown);

				if (line.Length < 3)
					return new ConnectionResponse(StatusCode.Unknown);

				int code;

				if (!int.TryParse(line.Substring(0, 3), out code))
					return new ConnectionResponse(StatusCode.Unknown);

				line = line.Substring(3);

				if (line.Length == 0)
					return new ConnectionResponse((StatusCode)code, new List<string>() { "" });

				if (line[0] != '+' && line[0] != '-')
				{
					if (line[0] == ' ')
						line = line.Substring(1);

					return new ConnectionResponse((StatusCode)code, new List<string>() { line });
				}

				char id = line[0];

				List<string> responses = new List<string>();
				responses.Add(line.Substring(1));

				try
				{
					for (line = reader.ReadLine(); line != null; line = reader.ReadLine())
					{
						string temp1 = line.Trim();
						string temp2 = temp1;

						if (temp1.Length == 0)
							continue;
						if (id == '-' && temp2.Length > 3 && temp2[3] == ' ')
							break;

						if (temp1.Length > 3 && id != '+')
							temp1 = temp1.Substring(4);

						responses.Add(temp1);

						if (id == '+' && ".".Equals(temp1))
							break;
					}
				}
				catch
				{
				}

				return new ConnectionResponse((StatusCode)code, responses);
			}
			catch
			{
				return new ConnectionResponse(StatusCode.Unknown);
			}
		}

		/// <summary>
		/// Writes a command to the connection and flushes the buffer to the control port.
		/// </summary>
		/// <param name="command">The command to write to the connection.</param>
		/// <returns><c>true</c> if the command is dispatched successfully; otherwise, <c>false</c>.</returns>
		public bool Write(string command)
		{
			if (command == null)
				throw new ArgumentNullException("command");

			if (!command.EndsWith(EOL))
				command += EOL;

			return Write(Encoding.ASCII.GetBytes(command));
		}

		/// <summary>
		/// Writes a command to the connection and flushes the buffer to the control port.
		/// </summary>
		/// <param name="command">The command to write to the connection.</param>
		/// <param name="parameters">An optional collection of parameters to serialize into the command.</param>
		/// <returns><c>true</c> if the command is dispatched successfully; otherwise, <c>false</c>.</returns>
		public bool Write(string command, params object[] parameters)
		{
			if (command == null)
				throw new ArgumentNullException("command");

			command = string.Format(command, parameters);

			if (!command.EndsWith(EOL))
				command += EOL;

			return Write(Encoding.ASCII.GetBytes(command));
		}

		/// <summary>
		/// Writes a command to the connection and flushes the buffer to the control port.
		/// </summary>
		/// <param name="buffer">The buffer containing the command data.</param>
		/// <returns><c>true</c> if the command is dispatched successfully; otherwise, <c>false</c>.</returns>
		public bool Write(byte[] buffer)
		{
			if (disposed)
				throw new ObjectDisposedException("this");
			if (buffer == null || buffer.Length == 0)
				throw new ArgumentNullException("buffer");
			if (socket == null || stream == null || reader == null)
				return false;

			try
			{
				stream.Write(buffer, 0, buffer.Length);
				stream.Flush();

				return true;
			}
			catch
			{
				return false;
			}
		}
	}
	/// <summary>
	/// A class containing information regarding a response received back from a control connection.
	/// </summary>
	internal sealed class ConnectionResponse
	{
		private readonly StatusCode code;
		private readonly ReadOnlyCollection<string> responses;

		/// <summary>
		/// Initializes a new instance of the <see cref="ConnectionResponse"/> class.
		/// </summary>
		/// <param name="code">The status code returned by the control connection.</param>
		public ConnectionResponse(StatusCode code)
		{
			this.code = code;
			responses = new List<string>().AsReadOnly();
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="ConnectionResponse"/> class.
		/// </summary>
		/// <param name="code">The status code returned by the control connection.</param>
		/// <param name="responses">The responses received back from the control connection.</param>
		public ConnectionResponse(StatusCode code, IList<string> responses)
		{
			this.code = code;
			this.responses = new ReadOnlyCollection<string>(responses);
		}

		#region Properties

		/// <summary>
		/// Gets a read-only collection of responses received from the control connection.
		/// </summary>
		public ReadOnlyCollection<string> Responses
		{
			get { return responses; }
		}

		/// <summary>
		/// Gets the status code returned with the response.
		/// </summary>
		public StatusCode StatusCode
		{
			get { return code; }
		}

		/// <summary>
		/// Gets a value indicating whether the response was successful feedback.
		/// </summary>
		public bool Success
		{
			get { return code == StatusCode.OK; }
		}

		#endregion
	}
}
