using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Palaso.Reporting;
using SayMore.Model.Files;
using SayMore.Properties;
using SayMore.Transcription.Model;
using SayMore.UI.ComponentEditors;
using SayMore.UI.MediaPlayer;
using SilTools;

namespace SayMore.Transcription.UI
{
	/// ----------------------------------------------------------------------------------------
	public partial class TextAnnotationEditor : EditorBase
	{
		//public delegate TextAnnotationEditor Factory(ComponentFile file, string imageKey);

		private readonly TextAnnotationEditorGrid _grid;
		private readonly VideoPanel _videoPanel;
		private FileSystemWatcher _watcher;
		private bool _isFirstTimeActivated = true;

		/// ------------------------------------------------------------------------------------
		public TextAnnotationEditor(ComponentFile file, string imageKey) : base(file, null, imageKey)
		{
			InitializeComponent();
			Name = "Annotations";
			_toolStrip.Renderer = new NoToolStripBorderRenderer();

			_comboPlaybackSpeed.Font = SystemFonts.IconTitleFont;
			// TODO: Internationalize
			_comboPlaybackSpeed.Items.AddRange(new[] { "100% (Normal)",
				"90%", "80%", "70%", "60%", "50%", "40%", "30%", "20%", "10%" });

			SetSpeedPercentageString(Settings.Default.AnnotationEditorPlaybackSpeed);
			_comboPlaybackSpeed.SelectedValueChanged += HandlePlaybackSpeedValueChanged;

			_grid = new TextAnnotationEditorGrid();
			_grid.Dock = DockStyle.Fill;
			_splitter.Panel2.Controls.Add(_grid);

			_videoPanel = new VideoPanel();
			_videoPanel.BackColor = Color.Black;
			_videoPanel.SetPlayerViewModel(_grid.PlayerViewModel);
			_splitter.Panel1.Controls.Add(_videoPanel);

			SetComponentFile(file);
			_splitter.Panel1.ClientSizeChanged += HandleSplitterPanel1ClientSizeChanged;

			_buttonHelp.Click += delegate
			{
				Program.ShowHelpTopic("/Using_Tools/Events_tab/Create_Annotation_File_overview.htm");
			};
		}

		/// ------------------------------------------------------------------------------------
		public override void SetComponentFile(ComponentFile file)
		{
			Deactivated();

			Utils.SetWindowRedraw(this, false);
			base.SetComponentFile(file);

			var annotationFile = file as AnnotationComponentFile;
			_splitter.Panel1Collapsed = annotationFile.GetIsAnnotatingAudioFile();

			var exception = annotationFile.TryLoadAndReturnException();
			if (exception != null)
			{
				var msg = Program.GetString("Transcription.UI.TextAnnotationEditor.LoadingAnnotationFileErrorMsg",
					"There was an error loading the annotation file '{0}'.");

				ErrorReport.NotifyUserOfProblem(exception, msg, file.PathToAnnotatedFile);
			}

			_grid.Load(annotationFile);
			SetupWatchingForFileChanges();
			Utils.SetWindowRedraw(this, true);
			_videoPanel.ShowVideoThumbnailNow();
		}

		/// ------------------------------------------------------------------------------------
		public override void Activated()
		{
			base.Activated();

			if (!_isFirstTimeActivated)
				return;

			_isFirstTimeActivated = false;

			if (Settings.Default.AnnotationEditorSpiltterPos > 0)
				_splitter.SplitterDistance = Settings.Default.AnnotationEditorSpiltterPos;

			_splitter.SplitterMoved += delegate
			{
				Settings.Default.AnnotationEditorSpiltterPos = _splitter.SplitterDistance;
			};
		}

		/// ------------------------------------------------------------------------------------
		public override void Deactivated()
		{
			if (_file != null)
			{
				_file.BeforeSave -= HandleBeforeAnnotationFileSaved;
				_file.AfterSave -= HandleAfterAnnotationFileSaved;
			}

			if (_watcher != null)
			{
				_watcher.Changed -= HandleAnnotationFileChanged;
				_watcher.Dispose();
				_watcher = null;
			}
		}

		/// ------------------------------------------------------------------------------------
		private void HandleSplitterPanel1ClientSizeChanged(object sender, EventArgs e)
		{
			Utils.SetWindowRedraw(this, false);

			_videoPanel.Size = new Size(_splitter.Panel1.ClientSize.Width,
				(int)(_splitter.Panel1.ClientSize.Width * Settings.Default.AnnotationEditorVideoWindowYtoXRatio));

			if (_videoPanel.Width > _splitter.Panel1.ClientSize.Width)
				_videoPanel.Width = _splitter.Panel1.ClientSize.Width;

			if (_videoPanel.Height > _splitter.Panel1.ClientSize.Height)
				_videoPanel.Height = _splitter.Panel1.ClientSize.Height;

			if (!_grid.PlayerViewModel.HasPlaybackStarted)
				_videoPanel.ShowVideoThumbnailNow();

			Utils.SetWindowRedraw(this, true);
		}

		/// ------------------------------------------------------------------------------------
		private void HandlePlaybackSpeedValueChanged(object sender, EventArgs e)
		{
			int percentage = GetSpeedPercentageFromText(_comboPlaybackSpeed.SelectedItem as string);
			Settings.Default.AnnotationEditorPlaybackSpeed = percentage;
			_grid.SetPlaybackSpeed(percentage);
		}

		/// ------------------------------------------------------------------------------------
		private int GetSpeedPercentageFromText(string text)
		{
			text = text ?? string.Empty;
			text = text.Replace("%", string.Empty).Trim();
			int percentage;
			return (int.TryParse(text, out percentage) ? percentage : 100);
		}

		/// ------------------------------------------------------------------------------------
		private void SetSpeedPercentageString(int percentage)
		{
			var text = (percentage == 0 || percentage == 100 ?
				_comboPlaybackSpeed.Items[0] as string : string.Format("{0}%", percentage));

			int i = _comboPlaybackSpeed.FindStringExact(text);
			_comboPlaybackSpeed.SelectedIndex = (i >= 0 ? i : 0);
		}

		/// ------------------------------------------------------------------------------------
		protected override void OnFormLostFocus()
		{
			base.OnFormLostFocus();
			OnEditorAndChildrenLostFocus();
		}

		/// ------------------------------------------------------------------------------------
		protected override void OnEditorAndChildrenLostFocus()
		{
			base.OnEditorAndChildrenLostFocus();
			_grid.Stop();
			_grid.Invalidate();
		}

		/// ------------------------------------------------------------------------------------
		private void HandleExportButtonClick(object sender, EventArgs e)
		{
			var file = (AnnotationComponentFile)_file;
			var mediaFileName = Path.GetFileName(file.GetPathToAssociatedMediaFile());

			using (var dlg = new ExportToFieldWorksInterlinearDlg(mediaFileName))
			{
				if (dlg.ShowDialog() == DialogResult.Cancel)
					return;

				var tier = file.Tiers.FirstOrDefault(t => t is TextTier);

				InterlinearXmlHelper.Save(dlg.FileName, mediaFileName,
					tier, dlg.TranscriptionWs.Id, dlg.FreeTranslationWs.Id);
			}
		}

		#region Methods for tracking changes to the EAF file outside of SayMore
		/// ------------------------------------------------------------------------------------
		void SetupWatchingForFileChanges()
		{
			_watcher = new FileSystemWatcher(
				Path.GetDirectoryName(_file.PathToAnnotatedFile),
				Path.GetFileName(_file.PathToAnnotatedFile));

			_watcher.IncludeSubdirectories = false;
			_watcher.EnableRaisingEvents = true;
			_watcher.Changed += HandleAnnotationFileChanged;

			_file.BeforeSave += HandleBeforeAnnotationFileSaved;
			_file.AfterSave += HandleAfterAnnotationFileSaved;
		}

		/// ------------------------------------------------------------------------------------
		private void HandleBeforeAnnotationFileSaved(object sender, EventArgs e)
		{
			if (_watcher != null)
				_watcher.EnableRaisingEvents = false;
		}

		/// ------------------------------------------------------------------------------------
		private void HandleAfterAnnotationFileSaved(object sender, EventArgs e)
		{
			if (_watcher != null)
				_watcher.EnableRaisingEvents = true;
		}

		/// ------------------------------------------------------------------------------------
		private void HandleAnnotationFileChanged(object sender, FileSystemEventArgs e)
		{
			Invoke(new EventHandler((s, args) =>
			{
				_file.Load();
				_grid.Load(_file as AnnotationComponentFile);
			}));
		}

		#endregion

		/// ------------------------------------------------------------------------------------
		private void HandleRecordedAnnotationButtonClick(object sender, EventArgs e)
		{
			var annotationType = (sender == _buttonCarefulSpeech ?
				OralAnnotationType.Careful : OralAnnotationType.Translation);

			var file = ((AnnotationComponentFile)_file);
			var tier = (TimeOrderTier)file.Tiers.FirstOrDefault(t => t is TimeOrderTier);

			using (var dlg = new OralAnnotationDlg(annotationType, tier))
				dlg.ShowDialog();

			bool oralAnnotationFileAlreadyExist =
				(file.AssociatedComponentFile.GetOralAnnotationFile() != null);

			var oralAnnotationFile = OralAnnotationFileGenerator.Generate(tier, this);

			if (oralAnnotationFile != null && !oralAnnotationFileAlreadyExist &&
				ComponentFileListRefreshAction != null)
			{
				ComponentFileListRefreshAction(oralAnnotationFile);
			}
		}

		/// ------------------------------------------------------------------------------------
		private void HandleResegmentButtonClick(object sender, EventArgs e)
		{
			var msg = Program.GetString("Transcription.UI.TextAnnotationEditor.RegeneratingSegmentsWarningMsg",
				"Regenerating segments will cause all oral and written annotations to be lost.\nAre you sure you want to continue?");

			if (MessageBox.Show(msg, Application.ProductName, MessageBoxButtons.YesNo) == DialogResult.No)
				return;

			// TODO: delete oral annoations

			Deactivated();
			var file = ((AnnotationComponentFile)_file);
			file.AssociatedComponentFile.CreateAnnotationFile(null);
			SetComponentFile(_file);
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Update the tab text in case it was localized.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		protected override void HandleStringsLocalized()
		{
			TabText = Program.GetString("Transcription.UI.TextAnnotationEditor.TabText", "Annotations");
			base.HandleStringsLocalized();
		}
	}
}
