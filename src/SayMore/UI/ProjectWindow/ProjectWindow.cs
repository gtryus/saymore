using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Localization;
using Localization.UI;
using Palaso.Media;
using SayMore.Properties;
using SayMore.UI.ElementListScreen;
using SilUtils;

namespace SayMore.UI.ProjectWindow
{
	/// ----------------------------------------------------------------------------------------
	/// <summary>
	/// Class for the main window of the application.
	/// </summary>
	/// ----------------------------------------------------------------------------------------
	public partial class ProjectWindow : Form
	{
		public delegate ProjectWindow Factory(string projectName); //autofac uses this

		private readonly string _projectName;
		private readonly IEnumerable<ICommand> _commands;

		public bool UserWantsToOpenADifferentProject { get; set; }

		/// ------------------------------------------------------------------------------------
		private ProjectWindow()
		{
			InitializeComponent();
		}

		/// ------------------------------------------------------------------------------------
		public ProjectWindow(string projectName, EventsListScreen eventsScreen,
			PersonListScreen personsScreen, Overview.OverviewScreen  overviewScreen,
			IEnumerable<ICommand> commands) : this()
		{
			if (Settings.Default.ProjectWindow == null)
			{
				StartPosition = FormStartPosition.CenterScreen;
				Settings.Default.ProjectWindow = FormSettings.Create(this);
			}

			var asm = Assembly.GetExecutingAssembly();
			Icon = new Icon (asm.GetManifestResourceStream("SayMore.SayMore.ico"));

			_projectName = Path.GetFileNameWithoutExtension(projectName);
			_commands = commands;

			_viewTabGroup.AddTab("Events", eventsScreen);
			_viewTabGroup.AddTab("People", personsScreen);
			_viewTabGroup.AddTab("Progress", overviewScreen);
			//_viewTabGroup.AddTab("Send/Receive", new SendReceiveScreen());

			SetWindowText();
			LocalizeItemDlg.StringsLocalized += SetWindowText;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Sets the localized window title texts.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		private void SetWindowText()
		{
			var ver = Assembly.GetExecutingAssembly().GetName().Version;
			var fmt = LocalizationManager.GetString(this);
			Text = string.Format(fmt, _projectName, ver.Major, ver.Minor, ver.Build);
		}

		/// ------------------------------------------------------------------------------------
		protected override void OnLoad(EventArgs e)
		{
			Settings.Default.ProjectWindow.InitializeForm(this);
			base.OnLoad(e);

			_viewTabGroup.SetActiveView(_viewTabGroup.Tabs[0]);

			if (!MediaInfo.HaveNecessaryComponents)
				new MissingFFmpegPopup().Show();
		}

		/// ------------------------------------------------------------------------------------
		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			base.OnFormClosing(e);
			LocalizeItemDlg.StringsLocalized -= SetWindowText;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Show the welcome dialog to allow the user to choose another project.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		private void HandleOpenProjectClick(object sender, EventArgs e)
		{
			UserWantsToOpenADifferentProject = true;
			Close();
		}

		/// ------------------------------------------------------------------------------------
		private void HandleExitClick(object sender, EventArgs e)
		{
			UserWantsToOpenADifferentProject = false;
			Close();
		}

		/// ------------------------------------------------------------------------------------
		private void HandleHelpAboutClick(object sender, EventArgs e)
		{
			using (var dlg = new AboutDialog())
				dlg.ShowDialog();
		}

		/// ------------------------------------------------------------------------------------
		private void OnCommandMenuItem_Click(object sender, EventArgs e)
		{
			var handler = _commands.First(c => c.Id == (string)((ToolStripMenuItem)sender).Tag);
			handler.Execute();
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Draw a subtle line at the bottom of the main menu to visually separate
		/// the main tabs from the main menu.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		private void HandleMainMenuPaint(object sender, PaintEventArgs e)
		{
			var clr = Color.FromArgb(30, Color.Black);
			using (var pen = new Pen(clr))
			{
				var rc = _mainMenuStrip.ClientRectangle;
				e.Graphics.DrawLine(pen, 0, rc.Bottom - 1, rc.Right, rc.Bottom - 1);
			}
		}
	}
}
