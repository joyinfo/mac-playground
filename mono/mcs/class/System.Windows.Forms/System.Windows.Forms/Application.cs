// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// Copyright (c) 2004 - 2006 Novell, Inc.
//
// Authors:
//	Peter Bartok	pbartok@novell.com
//      Daniel Nauck    (dna(at)mono-project(dot)de)
//

// COMPLETE

#undef DebugRunLoop

using Microsoft.Win32;
using System;
using System.Drawing;
using System.ComponentModel;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using System.Windows.Forms.VisualStyles;

namespace System.Windows.Forms
{
	public sealed class Application
	{
		internal class MWFThread
		{
			#region Fields

			private ApplicationContext context;
			private bool messageloop_started;
			private bool handling_exception;
			private int thread_id;

			private static readonly Hashtable threads = new Hashtable();

			#endregion	// Fields

			#region Constructors

			private MWFThread()
			{
			}

			#endregion	// Constructors

			#region Properties

			public ApplicationContext Context {
				get { return context; }
				set { context = value; }
			}

			public bool MessageLoop {
				get { return messageloop_started; }
				set { messageloop_started = value; }
			}

			public bool HandlingException {
				get { return handling_exception; }
				set { handling_exception = value; }
			}

			public static int LoopCount {
				get {
					lock (threads) {
						int loops = 0;

						foreach (MWFThread thread in threads.Values) {
							if (thread.messageloop_started)
								loops++;
						}

						return loops;
					}
				}
			}

			public static MWFThread Current {
				get {
					MWFThread thread = null;

					lock (threads) {
						thread = (MWFThread) threads [Thread.CurrentThread.GetHashCode ()];
						if (thread == null) {
							thread = new MWFThread();
							thread.thread_id = Thread.CurrentThread.GetHashCode ();
							threads [thread.thread_id] = thread;
						}
					}

					return thread;
				}
			}

			#endregion	// Properties

			#region Methods

			public void Exit ()
			{
				if (context != null)
					context.ExitThread();
				context = null;

				if (Application.ThreadExit != null)
					Application.ThreadExit(null, EventArgs.Empty);

				if (LoopCount == 0) {
					if (Application.ApplicationExit != null)
						Application.ApplicationExit (null, EventArgs.Empty);
				}

				((MWFThread) threads [thread_id]).MessageLoop = false;
			}

			#endregion	// Methods
		}

		private static bool browser_embedded;
		private static InputLanguage input_language = InputLanguage.CurrentInputLanguage;
		private static string safe_caption_format = "{1} - {0} - {2}";
		private static readonly ArrayList message_filters = new ArrayList();
		private static readonly FormCollection forms = new FormCollection ();

		private static bool use_wait_cursor;
		private static ToolStrip keyboard_capture;
		private static VisualStyleState visual_style_state = VisualStyleState.ClientAndNonClientAreasEnabled;
		static bool visual_styles_enabled;

		private Application ()
		{
			browser_embedded = false;
		}

		static Application ()
		{
			// Attempt to load UIA support for winforms
			// UIA support requires .NET 2.0
			InitializeUIAutomation ();
		}

		#region Private Methods

		private static void InitializeUIAutomation ()
		{
			// Initialize the UIAutomationWinforms Global class,
			// which create some listeners which subscribe to internal
			// MWF events so that it can provide a11y support for MWF
			const string UIA_WINFORMS_ASSEMBLY = 
			  "UIAutomationWinforms, Version=1.0.0.0, Culture=neutral, PublicKeyToken=f4ceacb585d99812";
			MethodInfo init_method;
			Assembly mwf_providers = null;
			try {
				mwf_providers = Assembly.Load (UIA_WINFORMS_ASSEMBLY);
			} catch { }
			
			if (mwf_providers == null)
				return;

			const string UIA_WINFORMS_TYPE     = "Mono.UIAutomation.Winforms.Global";
			const string UIA_WINFORMS_METHOD   = "Initialize";
			try {
				Type global_type = mwf_providers.GetType (UIA_WINFORMS_TYPE, false);
				if (global_type != null) {
					init_method = global_type.GetMethod (UIA_WINFORMS_METHOD, 
					                                     BindingFlags.Static | 
					                                     BindingFlags.Public);
					if (init_method != null)
						init_method.Invoke (null, new object [] {});
					else
						throw new Exception (String.Format ("Method {0} not found in type {1}.",
						                                    UIA_WINFORMS_METHOD, UIA_WINFORMS_TYPE));
				}
				else
					throw new Exception (String.Format ("Type {0} not found in assembly {1}.",
					                                    UIA_WINFORMS_TYPE, UIA_WINFORMS_ASSEMBLY));
			} catch (Exception ex) {
				Console.Error.WriteLine ("Error setting up UIA: " + ex);
			}
		}
		
		internal static void CloseForms (Thread thread)
		{
			#if DebugRunLoop
				Console.WriteLine("   CloseForms({0}) called", thread);
			#endif

			ArrayList forms_to_close = new ArrayList ();

			lock (forms) {
				foreach (Form f in forms) {
					if (thread == null || thread == f.creator_thread)
						forms_to_close.Add (f);
				}

				foreach (Form f in forms_to_close) {
					#if DebugRunLoop
						Console.WriteLine("      Closing form {0}", f);
					#endif
					f.Dispose ();
				}
			}
		}

		#endregion	// Private methods

		#region Public Static Properties

		public static bool AllowQuit {
			get {
				return !browser_embedded;
			}
		}

		public static string CommonAppDataPath {
			get {
				return CreateDataPath (Environment.GetFolderPath (Environment.SpecialFolder.CommonApplicationData));
			}
		}

		public static RegistryKey CommonAppDataRegistry {
			get {
				string key = string.Format ("Software\\{0}\\{1}\\{2}", CompanyName, ProductName, ProductVersion);

				return Registry.LocalMachine.CreateSubKey (key);
			}
		}

		public static string CompanyName {
			get {
				string company = string.Empty;

				Assembly assembly = Assembly.GetEntryAssembly ();
				
				if (assembly == null)
					assembly = Assembly.GetCallingAssembly ();

				AssemblyCompanyAttribute[] attrs = (AssemblyCompanyAttribute[])
					assembly.GetCustomAttributes (typeof(AssemblyCompanyAttribute), true);
				if (attrs != null && attrs.Length > 0)
					company = attrs [0].Company;

				// If there is no [AssemblyCompany], return the outermost namespace
				// on Main ()
				if (company == null || company.Length == 0)
					if (assembly.EntryPoint != null) {
						company = assembly.EntryPoint.DeclaringType.Namespace;

						if (company != null) {
							int firstDot = company.IndexOf ('.');
							if (firstDot >= 0)
								company = company.Substring (0, firstDot);
						}
					}

				// If that doesn't work, return the name of class containing Main ()
				if (company == null || company.Length == 0)
					if (assembly.EntryPoint != null)
						company = assembly.EntryPoint.DeclaringType.FullName;
				
				return company;
			}
		}

		public static CultureInfo CurrentCulture {
			get {
				return Thread.CurrentThread.CurrentUICulture;
			}
			set {
				Thread.CurrentThread.CurrentUICulture = value;
			}
		}

		public static InputLanguage CurrentInputLanguage {
			get {
				return input_language;
			}
			set {
				input_language=value;
			}
		}

		public static string ExecutablePath {
			get {
				return Path.GetFullPath (Environment.GetCommandLineArgs ()[0]);
			}
		}

		public static string LocalUserAppDataPath {
			get {
				return CreateDataPath (Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData));
			}
		}

		public static bool MessageLoop {
			get {
				return MWFThread.Current.MessageLoop;
			}
		}

		public static string ProductName {
			get {
				string name = string.Empty;
				
				Assembly assembly = Assembly.GetEntryAssembly ();
				
				if (assembly == null)
					assembly = Assembly.GetCallingAssembly ();

				AssemblyProductAttribute[] attrs = (AssemblyProductAttribute[])
					assembly.GetCustomAttributes (typeof(AssemblyProductAttribute), true);

				if (attrs != null && attrs.Length > 0)
					name = attrs [0].Product;

				// If there is no [AssemblyProduct], .NET returns the name of 
				// the innermost namespace and if that fails, resorts to the 
				// name of the class containing Main ()
				if (name == null || name.Length == 0)
					if (assembly.EntryPoint != null) {
						name = assembly.EntryPoint.DeclaringType.Namespace;

						if (name != null) {
							int lastDot = name.LastIndexOf ('.');
							if (lastDot >= 0 && lastDot < name.Length - 1)
								name = name.Substring (lastDot + 1);
						}

						if (name == null || name.Length == 0)
							name = assembly.EntryPoint.DeclaringType.FullName;
					}

				return name;
			}
		}

		public static string ProductVersion {
			get {
				String version = string.Empty;

				Assembly assembly = Assembly.GetEntryAssembly ();
				
				if (assembly == null)
					assembly = Assembly.GetCallingAssembly ();

				AssemblyInformationalVersionAttribute infoVersion =
					Attribute.GetCustomAttribute (assembly,
					typeof (AssemblyInformationalVersionAttribute))
					as AssemblyInformationalVersionAttribute;
					
				if (infoVersion != null)
					version = infoVersion.InformationalVersion;

				// If [AssemblyFileVersion] is present it is used
				// before resorting to assembly version
				if (version == null || version.Length == 0) {
					AssemblyFileVersionAttribute fileVersion =
						Attribute.GetCustomAttribute (assembly,
						typeof (AssemblyFileVersionAttribute))
						as AssemblyFileVersionAttribute;
					if (fileVersion != null)
						version = fileVersion.Version;
				}

				// If neither [AssemblyInformationalVersionAttribute]
				// nor [AssemblyFileVersion] are present, then use
				// the assembly version
				if (version == null || version.Length == 0)
					version = assembly.GetName ().Version.ToString ();

				return version;
			}
		}

		public static string SafeTopLevelCaptionFormat {
			get {
				return safe_caption_format;
			}
			set {
				safe_caption_format = value;
			}
		}

		public static string StartupPath {
			get {
				return Path.GetDirectoryName (Application.ExecutablePath);
			}
		}

		public static string UserAppDataPath {
			get {
				return CreateDataPath (Environment.GetFolderPath (Environment.SpecialFolder.ApplicationData));
			}
		}

		public static RegistryKey UserAppDataRegistry {
			get {
				string key = string.Format ("Software\\{0}\\{1}\\{2}", CompanyName, ProductName, ProductVersion);
				
				return Registry.CurrentUser.CreateSubKey (key);
			}
		}

		public static bool UseWaitCursor {
			get {
				return use_wait_cursor;
			}
			set {
				use_wait_cursor = value;
				if (use_wait_cursor) {
					foreach (Form form in OpenForms) {
						form.Cursor = Cursors.WaitCursor;
					}
				}
			}
		}

		public static bool RenderWithVisualStyles {
			get {
				if (VisualStyleInformation.IsSupportedByOS) {
					if (!VisualStyleInformation.IsEnabledByUser)
						return false;
					if (!XplatUI.ThemesEnabled)
						return false;
					if (Application.VisualStyleState == VisualStyleState.ClientAndNonClientAreasEnabled)
						return true;
					if (Application.VisualStyleState == VisualStyleState.ClientAreaEnabled)
						return true;
				}
				return false;
			}
		}

		public static VisualStyleState VisualStyleState {
			get { return Application.visual_style_state; }
			set { Application.visual_style_state = value; }
		}

		#endregion

		#region Public Static Methods

		public static void AddMessageFilter (IMessageFilter value)
		{
			if (value != null) {
				lock (message_filters) {
					message_filters.Add (value);
				}
			}
		}

		internal static void AddKeyFilter (IKeyFilter value)
		{
			XplatUI.AddKeyFilter (value);
		}

		public static void DoEvents ()
		{
			XplatUI.DoEvents ();

		}

		public static void EnableVisualStyles ()
		{
			visual_styles_enabled = true;
			XplatUI.EnableThemes ();
		}

		[EditorBrowsable (EditorBrowsableState.Advanced)]
		public static bool FilterMessage (ref Message message)
		{
			lock (message_filters) {
				for (int i = 0; i < message_filters.Count; i++) {
					IMessageFilter filter = (IMessageFilter) message_filters[i];
					if (null != filter && filter.PreFilterMessage (ref message))
						return true;
				}
			}
			return false;
		}
		
		//
		// If true, it uses GDI+, performance reasons were quoted
		//
		static internal bool use_compatible_text_rendering = true;
		
		public static void SetCompatibleTextRenderingDefault (bool defaultValue)
		{
			use_compatible_text_rendering = defaultValue;
		}

		public static FormCollection OpenForms {
			get {
				return forms;
			}
		}

		internal static FormCollection OpenFormsInternal
		{
			get {
				return forms;
			}
		}

		[MonoNotSupported ("Only applies when Winforms is being hosted by an unmanaged app.")]
		[EditorBrowsable (EditorBrowsableState.Advanced)]
		public static void RegisterMessageLoop (MessageLoopCallback callback)
		{
		}

		[MonoNotSupported ("Empty stub.")]
		public static bool SetSuspendState (PowerState state, bool force, bool disableWakeEvent)
		{
			return false;
		}

		[MonoNotSupported ("Empty stub.")]
		public static void SetUnhandledExceptionMode (UnhandledExceptionMode mode)
		{
			//FIXME: a stub to fill
		}

		[MonoNotSupported ("Empty stub.")]
		public static void SetUnhandledExceptionMode (UnhandledExceptionMode mode, bool threadScope)
		{
			//FIXME: a stub to fill
		}

		[MonoNotSupported ("Only applies when Winforms is being hosted by an unmanaged app.")]
		[EditorBrowsable (EditorBrowsableState.Advanced)]
		public static void UnregisterMessageLoop ()
		{
		}

		[EditorBrowsable (EditorBrowsableState.Advanced)]
		public static void RaiseIdle (EventArgs e)
		{
			XplatUI.RaiseIdle (e);
		}
		
		public static void Restart ()
		{
			//FIXME: ClickOnce stuff using the Update or UpdateAsync methods.
			//FIXME: SecurityPermission: Restart () requires IsUnrestricted permission.

			if (Assembly.GetEntryAssembly () == null)
				throw new NotSupportedException ("The method 'Restart' is not supported by this application type.");

			string mono_path = null;

			//Get mono path
			PropertyInfo gac = typeof (Environment).GetProperty ("GacPath", BindingFlags.Static | BindingFlags.NonPublic);
			MethodInfo get_gac = null;
			if (gac != null)
				get_gac = gac.GetGetMethod (true);

			if (get_gac != null) {
				string gac_path = Path.GetDirectoryName ((string)get_gac.Invoke (null, null));
				string mono_prefix = Path.GetDirectoryName (Path.GetDirectoryName (gac_path));

				if (XplatUI.RunningOnUnix) {
					mono_path = Path.Combine (mono_prefix, "bin/mono");
					if (!File.Exists (mono_path))
						mono_path = "mono";
				} else {
					mono_path = Path.Combine (mono_prefix, "bin\\mono.bat");

					if (!File.Exists (mono_path))
						mono_path = Path.Combine (mono_prefix, "bin\\mono.exe");

					if (!File.Exists (mono_path))
						mono_path = Path.Combine (mono_prefix, "mono\\mono\\mini\\mono.exe");

					if (!File.Exists (mono_path))
						throw new FileNotFoundException (string.Format ("Windows mono path not found: '{0}'", mono_path));
				}
			}

			//Get command line arguments
			StringBuilder argsBuilder = new StringBuilder ();
			string[] args = Environment.GetCommandLineArgs ();
			for (int i = 0; i < args.Length; i++)
			{
				argsBuilder.Append (string.Format ("\"{0}\" ", args[i]));
			}
			string arguments = argsBuilder.ToString ();
			ProcessStartInfo procInfo = Process.GetCurrentProcess ().StartInfo;

			if (mono_path == null) { //it is .NET on Windows
				procInfo.FileName = args[0];
				procInfo.Arguments = arguments.Remove (0, args[0].Length + 3); //1 space and 2 quotes
			}
			else {
				procInfo.Arguments = arguments;
				procInfo.FileName = mono_path;
			}

			procInfo.WorkingDirectory = Environment.CurrentDirectory;

			Application.Exit ();
			Process.Start (procInfo);
		}

		public static void Exit ()
		{
			Exit (new CancelEventArgs ());
		}

		[EditorBrowsable (EditorBrowsableState.Advanced)]
		public static void Exit (CancelEventArgs e)
		{
			ArrayList forms_to_close;
			
			lock (forms) {
				forms_to_close = new ArrayList (forms);

				foreach (Form f in forms_to_close) {
					// Give each form a chance to cancel the Application.Exit
					e.Cancel = f.FireClosingEvents (CloseReason.ApplicationExitCall, false);

					if (e.Cancel)
						return;

					f.suppress_closing_events = true;
					f.Close ();
					f.Dispose ();
				}
			}

			XplatUI.PostQuitMessage (0);
		}

		public static void ExitThread()
		{
			CloseForms(Thread.CurrentThread);
			// this might not be right - need to investigate (somehow) if a WM_QUIT message is generated here
			XplatUI.PostQuitMessage(0);
		}

		public static ApartmentState OleRequired ()
		{
			//throw new NotImplementedException("OLE Not supported by this System.Windows.Forms implementation");
			return ApartmentState.Unknown;
		}

		public static void OnThreadException (Exception t)
		{
			if (MWFThread.Current.HandlingException) {
				/* we're already handling an exception and we got
				   another one?  print it out and exit, this means
				   we've got a runtime/SWF bug. */
				Console.WriteLine (t);
				// Don't use Application.Exit here, since it may cause a stack overflow
				// in certain cases. It's however hard to reproduce since it seems to 
				// be depending on when the GC kicks in.
				Environment.Exit(1);
			}

			try {
				MWFThread.Current.HandlingException = true;

				if (Application.ThreadException != null) {
					Application.ThreadException(null, new ThreadExceptionEventArgs(t));
					return;
				}

				if (SystemInformation.UserInteractive) {
					Form form = new ThreadExceptionDialog (t);
					form.ShowDialog ();
				} else {
					Console.WriteLine (t.ToString ());
					Application.Exit ();
				}
			} finally {
				MWFThread.Current.HandlingException = false;
			}
		}

		public static void RemoveMessageFilter (IMessageFilter value)
		{
			lock (message_filters) {
				message_filters.Remove (value);
			}
		}

		public static void Run ()
		{
			Run (new ApplicationContext ());
		}

		public static void Run (Form mainForm)
		{
			Run (new ApplicationContext (mainForm));
		}

		internal static void FirePreRun ()
		{
			EventHandler handler = PreRun;
			if (handler != null)
				handler (null, EventArgs.Empty);
		}

		public static void Run (ApplicationContext context)
		{
			// If a sync context hasn't been created by now, create
			// a default one
			if (SynchronizationContext.Current == null)
				SynchronizationContext.SetSynchronizationContext (new SynchronizationContext ());
				
			RunLoop (false, context);
			
			// Reset the sync context back to the default
			if (SynchronizationContext.Current is WindowsFormsSynchronizationContext)
				WindowsFormsSynchronizationContext.Uninstall ();
		}

		private static void DisableFormsForModalLoop (Queue toplevels, ApplicationContext context)
		{
			Form f;

			lock (forms) {
				IEnumerator control = forms.GetEnumerator ();

				while (control.MoveNext ()) {
					f = (Form)control.Current;

					// Don't disable the main form.
					if (f == context.MainForm) {
						continue;
					}

					// Don't disable any children of the main form.
					// These do not have to be MDI children.
					Control current = f;
					bool is_child_of_main = false; ;

					do {
						if (current.Parent == context.MainForm) {
							is_child_of_main = true;
							break;
						}
						current = current.Parent;
					} while (current != null);

					if (is_child_of_main)
						continue;

					// Disable the rest
					if (f.IsHandleCreated && XplatUI.IsEnabled (f.Handle)) {
#if DebugRunLoop
						Console.WriteLine("      Disabling form {0}", f);
#endif
						XplatUI.EnableWindow (f.Handle, false);
						toplevels.Enqueue (f);
					}
				}
			}
				
		}
		
		
		private static void EnableFormsForModalLoop (Queue toplevels, ApplicationContext context)
		{
			while (toplevels.Count > 0) {
#if DebugRunLoop
				Console.WriteLine("      Re-Enabling form form {0}", toplevels.Peek());
#endif
				Form c = (Form) toplevels.Dequeue ();
				if (c.IsHandleCreated) {
					XplatUI.EnableWindow (c.window.Handle, true);
					context.MainForm = c;
				}
			}
#if DebugRunLoop
			Console.WriteLine("   Done with the re-enable");
#endif
		}

		internal static void RunLoop (bool Modal, ApplicationContext context)
		{
			MWFThread thread = MWFThread.Current;
			
			// There is a NotWorking test for this, but since we are using this method both for Form.ShowDialog as for ApplicationContexts we'll
			// fail on nested ShowDialogs, so disable the check for the moment.
			//if (thread.MessageLoop) {
			//        throw new InvalidOperationException ("Starting a second message loop on a single thread is not a valid operation. Use Form.ShowDialog instead.");
			//}

			if (context == null)
				context = new ApplicationContext();
		
			ApplicationContext previous_thread_context = thread.Context;
			thread.Context = context;

			if (context.MainForm != null) {
				context.MainForm.is_modal = Modal;
				context.MainForm.context = context;
				context.MainForm.closing = false;
				context.MainForm.Visible = true;	// Cannot use Show() or scaling gets confused by menus
				// XXX the above line can be used to close the form. another problem with our handling of Show/Activate.
				if (context.MainForm != null)
					context.MainForm.Activate();
			}

			#if DebugRunLoop
				Console.WriteLine("Entering RunLoop(Modal={0}, Form={1})", Modal, context.MainForm != null ? context.MainForm.ToString() : "NULL");
			#endif

			Queue toplevels = new Queue();

			if (Modal) {
				DisableFormsForModalLoop (toplevels, context);
				
				// FIXME - need activate?
				// make sure the MainForm is enabled
				if (context.MainForm != null) {
					XplatUI.EnableWindow (context.MainForm.Handle, true);
					XplatUI.SetModal(context.MainForm.Handle, true);
				}
			}

			Object queue_id = XplatUI.StartLoop(Thread.CurrentThread);
			var prevMessageLoop = thread.MessageLoop;
			thread.MessageLoop = true;

			bool quit = false, drop = false;
			MSG msg = new MSG();
			while (!quit) {
				using (var cleanup = XplatUI.StartCycle(queue_id)) {

					if ((quit = !XplatUI.GetMessage(queue_id, ref msg, IntPtr.Zero, 0, 0)))
						continue;

					if (msg.message != Msg.WM_NULL)
						Application.SendMessage(ref msg, out drop, out quit);
					if (drop)
						continue;

					var mainForm = context.MainForm;
					if (mainForm == null)
						continue;
					
					// If our Form doesn't have a handle anymore, it means it was destroyed and we need to *wait* for WM_QUIT.
					if (!mainForm.IsHandleCreated)
						if (Modal) // ... but not in case of a modal dialog
							break;
						else
							continue;

					// Handle exit, Form might have received WM_CLOSE and set 'closing' in response.
					if (mainForm.closing || (Modal && !mainForm.Visible))
					{
						if (!Modal)
							XplatUI.PostQuitMessage(0);
						else
							break;
					}
				}
			}

			#if DebugRunLoop
				Console.WriteLine ("   RunLoop loop left");
			#endif

			thread.MessageLoop = prevMessageLoop;
			XplatUI.EndLoop (Thread.CurrentThread);

			if (Modal) {
				Form old = context.MainForm;
				context.MainForm = null;

				EnableFormsForModalLoop (toplevels, context);
				
				if (old != null && old.IsHandleCreated) {
					XplatUI.SetModal (old.Handle, false);
				}
				#if DebugRunLoop
					Console.WriteLine ("   Done with the SetModal");
				#endif
				old.RaiseCloseEvents (true, false);
				old.is_modal = false;
			}

			#if DebugRunLoop
				Console.WriteLine ("Leaving RunLoop(Modal={0}, Form={1})", Modal, context.MainForm != null ? context.MainForm.ToString() : "NULL");
			#endif

			if (context.MainForm != null) {
				context.MainForm.context = null;
				context.MainForm = null;
			}

			thread.Context = previous_thread_context;

			if (!Modal)
				thread.Exit();
		}

		internal static IntPtr SendMessage(IntPtr hwnd, Msg message, IntPtr wParam, IntPtr lParam)
		{
			bool drop, quit;
			MSG msg = new MSG { hwnd = hwnd, message = message, wParam = wParam, lParam = lParam };
			return Application.SendMessage(ref msg, out drop, out quit);
		}

		// Convenience wrapper
		internal static IntPtr SendMessage(ref MSG msg)
		{
			bool drop, quit;
			return Application.SendMessage(ref msg, out drop, out quit);
		}

		// Returns true if the message should be dropped.
		internal static IntPtr SendMessage(ref MSG msg, out bool drop, out bool quit)
		{
			drop = false;
			quit = false;
			Message m = Message.Create(msg.hwnd, (int)msg.message, msg.wParam, msg.lParam);
			if (Application.FilterMessage(ref m))
			{
				drop = true;
				return IntPtr.Zero;
			}

			switch ((Msg)msg.message)
			{
				case Msg.WM_KEYDOWN:
				case Msg.WM_SYSKEYDOWN:
				case Msg.WM_CHAR:
				case Msg.WM_SYSCHAR:
				case Msg.WM_KEYUP:
				case Msg.WM_SYSKEYUP:
					Control c = Control.FromHandle(msg.hwnd);

					// If we have a control with keyboard capture (usually a *Strip)
					// give it the message, and then drop the message
					if (keyboard_capture != null)
					{
						// WM_SYSKEYUP does not make it into ProcessCmdKey, so do it here
						if (msg.message == Msg.WM_SYSKEYDOWN)
						{
							if (m.WParam.ToInt32() == (int)Keys.Menu)
							{
								keyboard_capture.GetTopLevelToolStrip().Dismiss(ToolStripDropDownCloseReason.Keyboard);
								drop = true;
								return IntPtr.Zero;
							}
						}

						m.HWnd = keyboard_capture.Handle;

						switch (keyboard_capture.PreProcessControlMessageInternal(ref m))
						{
							case PreProcessControlState.MessageProcessed:
								drop = true;
								return IntPtr.Zero;
							case PreProcessControlState.MessageNeeded:
							case PreProcessControlState.MessageNotNeeded:
								if (((msg.message == Msg.WM_KEYDOWN || msg.message == Msg.WM_CHAR) && keyboard_capture != null && !keyboard_capture.ProcessControlMnemonic((char)m.WParam)))
								{
									if (c == null || !ControlOnToolStrip(c))
									{
										drop = true;
										return IntPtr.Zero;
									}
									m.HWnd = msg.hwnd;
								}
								else
								{
									drop = true;
									return IntPtr.Zero;
								}
								break;
						}
					}

					if (((c != null) && c.PreProcessControlMessageInternal(ref m) != PreProcessControlState.MessageProcessed) ||
						(c == null))
					{
						goto default;
					}
					return new IntPtr(1); // Drop

				case Msg.WM_LBUTTONDOWN:
				case Msg.WM_MBUTTONDOWN:
				case Msg.WM_RBUTTONDOWN:
					if (keyboard_capture != null) {
						Control c2 = Control.FromHandle (msg.hwnd);

						// The target is not a winforms control (an embedded control, perhaps), so
						// release everything
						if (c2 == null) {
							ToolStripManager.FireAppClicked ();
							goto default;
						}

						// Skip clicks on owner windows, eg. expanded ComboBox
						if (Control.IsChild (keyboard_capture.Handle, msg.hwnd)) {
							goto default;
						}

						// Close any active toolstrips drop-downs if we click outside of them,
						// but also don't close them all if we click outside of the top-most
						// one, but into its owner.
						Point c2_point = c2.PointToScreen (new Point (
							(int)(short)(m.LParam.ToInt32() & 0xffff),
							(int)(short)(m.LParam.ToInt32() >> 16)));
						while (keyboard_capture != null && !keyboard_capture.ClientRectangle.Contains (keyboard_capture.PointToClient (c2_point))) {
							keyboard_capture.Dismiss ();
						}
					}
					goto default;

				case Msg.WM_QUIT:
					quit = true; // make sure we exit
					break;
				default:
					XplatUI.TranslateMessage(ref msg);
					return XplatUI.DispatchMessage(ref msg);
			}

			return IntPtr.Zero;
		}

		#endregion	// Public Static Methods

		#region Events

		public static event EventHandler ApplicationExit;

		public static event EventHandler Idle {
			add {
				XplatUI.Idle += value;
			}
			remove {
				XplatUI.Idle -= value;
			}
		}

		public static event EventHandler ThreadExit;
		public static event ThreadExceptionEventHandler ThreadException;
		
		// These are used externally by the UIA framework
		internal static event EventHandler FormAdded;
		internal static event EventHandler PreRun;

#pragma warning disable 0067
		[MonoTODO]
		[EditorBrowsable (EditorBrowsableState.Advanced)]
		public static event EventHandler EnterThreadModal;

		[MonoTODO]
		[EditorBrowsable (EditorBrowsableState.Advanced)]
		public static event EventHandler LeaveThreadModal;
#pragma warning restore 0067

		#endregion	// Events

		#region Public Delegates

		[EditorBrowsable (EditorBrowsableState.Advanced)]
		public delegate bool MessageLoopCallback ();

		#endregion
		
		#region Internal Properties
		internal static ToolStrip KeyboardCapture {
			get { return keyboard_capture; }
			set { keyboard_capture = value; }
		}

		internal static bool VisualStylesEnabled {
			get { return visual_styles_enabled; }
		}
		#endregion
		
		#region Internal Methods

		internal static void AddForm (Form f)
		{
			lock (forms)
				forms.Add (f);
			// Signal that a Form has been added to this
			// Application. Used by UIA to detect new Forms that
			// need a11y support. This event may be fired even if
			// the form has already been added, so clients should
			// account for that when handling this signal.
			if (FormAdded != null)
				FormAdded (f, null);
		}
		
		internal static void RemoveForm (Form f)
		{
			lock (forms)
				forms.Remove (f);
		}

		private static bool ControlOnToolStrip (Control c)
		{
			Control p = c.Parent;
			
			while (p != null) {
				if (p is ToolStrip)
					return true;
					
				p = p.Parent;
			}
			
			return false;
		}

		// Takes a starting path, appends company name, product name, and
		// product version.  If the directory doesn't exist, create it
		private static string CreateDataPath (string basePath)
		{
			string path;

			path = Path.Combine (basePath, CompanyName);
			path = Path.Combine (path, ProductName);
			path = Path.Combine (path, ProductVersion);

			if (!Directory.Exists (path))
				Directory.CreateDirectory (path);
			
			return path;
		}
		#endregion
	}
}
