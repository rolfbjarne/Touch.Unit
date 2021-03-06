// Main.cs: Touch.Unit Simple Server
//
// Authors:
//	Sebastien Pouliot  <sebastien@xamarin.com>
//
// Copyright 2011 Xamarin Inc. All rights reserved

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.Threading;

using Mono.Options;

// a simple, blocking (i.e. one device/app at the time), listener
class SimpleListener {

	static byte[] buffer = new byte [16 * 1024];

	IPAddress Address { get; set; }
	int Port { get; set; }
	string LogPath { get; set; }
	string LogFile { get; set; }
	bool AutoExit { get; set; }

	public void Start ()
	{
		bool processed;
		
		Console.WriteLine ("Touch.Unit Simple Server listening on: {0}:{1}", Address, Port);
		TcpListener server = new TcpListener (Address, Port);
		try {
			server.Start ();
			
			do {
				using (TcpClient client = server.AcceptTcpClient ()) {
					processed = Processing (client);
				}
			} while (!AutoExit || !processed);
		}
		catch (Exception e) {
			Console.WriteLine ("[{0}] : {1}", DateTime.Now, e);
		}
		finally {
			server.Stop ();
		}
	}

	public bool Processing (TcpClient client)
	{
		string logfile = Path.Combine (LogPath, LogFile ?? DateTime.UtcNow.Ticks.ToString () + ".log");
		string remote = client.Client.RemoteEndPoint.ToString ();
		Console.WriteLine ("Connection from {0} saving logs to {1}", remote, logfile);

		using (FileStream fs = File.OpenWrite (logfile)) {
			// a few extra bits of data only available from this side
			string header = String.Format ("[Local Date/Time:\t{1}]{0}[Remote Address:\t{2}]{0}", 
				Environment.NewLine, DateTime.Now, remote);
			byte[] array = Encoding.UTF8.GetBytes (header);
			fs.Write (array, 0, array.Length);
			fs.Flush ();
			// now simply copy what we receive
			int i;
			int total = 0;
			NetworkStream stream = client.GetStream ();
			while ((i = stream.Read (buffer, 0, buffer.Length)) != 0) {
				fs.Write (buffer, 0, i);
				fs.Flush ();
				total += i;
			}
			
			if (total < 16) {
				// This wasn't a test run, but a connection from the app (on device) to find
				// the ip address we're reachable on.
				return false;
			}
		}
		
		return true;
	}

	static void ShowHelp (OptionSet os)
	{
		Console.WriteLine ("Usage: mono Touch.Server.exe [options]");
		os.WriteOptionDescriptions (Console.Out);
	}

	public static void Main (string[] args)
	{ 
		Console.WriteLine ("Touch.Unit Simple Server");
		Console.WriteLine ("Copyright 2011, Xamarin Inc. All rights reserved.");
		
		bool help = false;
		string address = null;
		string port = null;
		string log_path = ".";
		string log_file = null;
		string launchdev = null;
		string launchsim = null;
		bool autoexit = false;
		
		var os = new OptionSet () {
			{ "h|?|help", "Display help", v => help = true },
			{ "ip", "IP address to listen (default: Any)", v => address = v },
			{ "port", "TCP port to listen (default: 16384)", v => port = v },
			{ "logpath", "Path to save the log files (default: .)", v => log_path = v },
			{ "logfile=", "Filename to save the log to (default: automatically generated)", v => log_file = v },
			{ "launchdev=", "Run the specified app on a device (specify using bundle identifier)", v => launchdev = v },
			{ "launchsim=", "Run the specified app on the simulator (specify using path to *.app directory)", v => launchsim = v },
			{ "autoexit", "Exit the server once a test run has completed (default: false)", v => autoexit = true },
		};
		
		try {
			os.Parse (args);
			if (help)
				ShowHelp (os);
			
			var listener = new SimpleListener ();
			
			IPAddress ip;
			if (String.IsNullOrEmpty (address) || !IPAddress.TryParse (address, out ip))
				listener.Address = IPAddress.Any;
			
			ushort p;
			if (UInt16.TryParse (port, out p))
				listener.Port = p;
			else
				listener.Port = 16384;
			
			listener.LogPath = log_path ?? ".";
			listener.LogFile = log_file;
			listener.AutoExit = autoexit;
			
			if (launchdev != null) {
				ThreadPool.QueueUserWorkItem ((v) => {
					using (Process proc = new Process ()) {
						StringBuilder procArgs = new StringBuilder ();
						procArgs.Append ("--launchdev ");
						procArgs.Append (launchdev);
						procArgs.Append (" -argument=-connection-mode -argument=none");
						procArgs.Append (" -argument=-app-arg:-autostart");
						procArgs.Append (" -argument=-app-arg:-autoexit");
						procArgs.Append (" -argument=-app-arg:-enablenetwork");
						procArgs.AppendFormat (" -argument=-app-arg:-hostport:{0}", listener.Port);
						procArgs.Append (" -argument=-app-arg:-hostname:");
						var ipAddresses = System.Net.Dns.GetHostEntry (System.Net.Dns.GetHostName ()).AddressList;
						for (int i = 0; i < ipAddresses.Length; i++) {
							if (i > 0)
								procArgs.Append (',');
							procArgs.Append (ipAddresses [i].ToString ());
						}
						proc.StartInfo.FileName = "/Developer/MonoTouch/usr/bin/mtouch";
						proc.StartInfo.Arguments = procArgs.ToString ();
						proc.Start ();
						proc.WaitForExit ();
					}
				});
			}
			
			if (launchsim != null) {
				ThreadPool.QueueUserWorkItem ((v) => {
					using (Process proc = new Process ()) {
						StringBuilder output = new StringBuilder ();
						StringBuilder procArgs = new StringBuilder ();
						procArgs.Append ("--launchsim ");
						procArgs.Append (launchsim);
						procArgs.Append (" -sdk 5.0");
						procArgs.Append (" -argument=-connection-mode -argument=none");
						procArgs.Append (" -argument=-app-arg:-autostart");
						procArgs.Append (" -argument=-app-arg:-autoexit");
						procArgs.Append (" -argument=-app-arg:-enablenetwork");
						procArgs.Append (" -argument=-app-arg:-hostname:127.0.0.1");
						procArgs.AppendFormat (" -argument=-app-arg:-hostport:{0}", listener.Port);
						proc.StartInfo.FileName = "/Developer/MonoTouch/usr/bin/mtouch";
						proc.StartInfo.Arguments = procArgs.ToString ();
						proc.StartInfo.UseShellExecute = false;
						proc.StartInfo.RedirectStandardError = true;
						proc.StartInfo.RedirectStandardOutput = true;
						proc.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) {
							lock (output)
								output.AppendLine (e.Data);
						};
						proc.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) {
							lock (output)
								output.AppendLine (e.Data);
						};
						proc.Start ();
						proc.BeginErrorReadLine ();
						proc.BeginOutputReadLine ();
						proc.WaitForExit ();
						if (proc.ExitCode != 0)
							Console.WriteLine (output.ToString ());
					}
				});
			}
			
			listener.Start ();
		} catch (OptionException oe) {
			Console.WriteLine ("{0} for options '{1}'", oe.Message, oe.OptionName);
		}
	}   
}