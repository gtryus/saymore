using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using Localization;
using Palaso.Reporting;
using SayMore.UI;
using SayMore.UI.LowLevelControls;

namespace SayMore.Transcription.Model
{
	public class InterlinearXmlHelper : IProgressViewModel
	{
		public event EventHandler OnFinished;
		public event EventHandler OnUpdateProgress;
		public event EventHandler OnUpdateStatus;

		private readonly string _wsTranscriptionId;
		private readonly string _wsFreeTranslationId;
		private readonly string _title;
		private readonly ITier _transcriptionTier;
		private readonly string _outputFilePath;
		private BackgroundWorker _worker;

		/// ------------------------------------------------------------------------------------
		public InterlinearXmlHelper(string outputFilePath, string title,
			ITier transcriptionTier, string wsTranscriptionId, string wsFreeTranslationId)
		{
			_outputFilePath = outputFilePath;
			_title = title;
			_transcriptionTier = transcriptionTier;
			_wsTranscriptionId = wsTranscriptionId;
			_wsFreeTranslationId = wsFreeTranslationId;
		}

		#region IProgressViewModel implementation
		/// ------------------------------------------------------------------------------------
		public int MaximumProgressValue
		{
			get { return _transcriptionTier.GetAllSegments().Count(); }
		}

		/// ------------------------------------------------------------------------------------
		public int CurrentProgressValue { get; private set; }

		/// ------------------------------------------------------------------------------------
		public string StatusString { get; private set; }

		/// ------------------------------------------------------------------------------------
		public void Start()
		{
			_worker = new BackgroundWorker();
			_worker.WorkerSupportsCancellation = true;
			_worker.WorkerReportsProgress = true;
			_worker.ProgressChanged += HandleWorkerProgressChanged;
			_worker.RunWorkerCompleted += HandleWorkerFinished;
			_worker.DoWork += BuildXml;
			_worker.RunWorkerAsync();
			while (_worker.IsBusy) { Application.DoEvents(); }
		}

		#endregion

		/// ------------------------------------------------------------------------------------
		private void BuildXml(object sender, DoWorkEventArgs e)
		{
			if (OnUpdateStatus != null)
			{
				StatusString = LocalizationManager.GetString(
					"EventsView.Transcription.TextAnnotationEditor.ExportingToFLExInterlinear.ProgressDlg.ProgressMsg",
					"Exporting...");

				OnUpdateStatus(this, EventArgs.Empty);
			}

			GetPopulatedRootElement().Save(_outputFilePath);
		}

		/// ------------------------------------------------------------------------------------
		void HandleWorkerProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			if (OnUpdateProgress != null)
			{
				CurrentProgressValue = e.ProgressPercentage;
				OnUpdateProgress(this, EventArgs.Empty);
			}
		}

		/// ------------------------------------------------------------------------------------
		void HandleWorkerFinished(object sender, RunWorkerCompletedEventArgs e)
		{
			if (OnFinished != null)
			{
				StatusString = LocalizationManager.GetString(
					"EventsView.Transcription.TextAnnotationEditor.ExportingToFLExInterlinear.ProgressDlg.FinsihedMsg",
					"Finished Exporting");

				OnFinished.Invoke(null, null);
			}
		}

		/// ------------------------------------------------------------------------------------
		public XElement GetPopulatedRootElement()
		{
			var rootElement = CreateRootElement();

			rootElement.Element("interlinear-text").Element("paragraphs").Add(
				CreateParagraphElements(_transcriptionTier));

			rootElement.Element("interlinear-text").Add(CreateLanguagesElement(
				new[] { _wsTranscriptionId, _wsFreeTranslationId }));

			return rootElement;
		}

		/// ------------------------------------------------------------------------------------
		public XElement CreateRootElement()
		{
			return new XElement("document", new XElement("interlinear-text",
				CreateItemElement(_wsFreeTranslationId, "title", _title),
				new XElement("paragraphs")));
		}

		/// ------------------------------------------------------------------------------------
		public XElement CreateLanguagesElement(IEnumerable<string> langIds)
		{
			var element = new XElement("languages");

			if (langIds != null)
			{
				foreach (var id in langIds)
					element.Add(new XElement("language", new XAttribute("lang", id)));
			}

			return element;
		}

		/// ------------------------------------------------------------------------------------
		public IEnumerable<XElement> CreateParagraphElements(ITier tier)
		{
			// TODO: This will need refactoring when display name is localizable.
			var translationTier =
				tier.DependentTiers.FirstOrDefault(t => t.DisplayName.ToLower() == TextTier.SayMoreFreeTranslationTierName.ToLower());

			var segmentList = tier.GetAllSegments().Cast<ITextSegment>().ToArray();

			for (int i = 0; i < segmentList.Length; i++)
			{
				// _worker will be null during tests.
				if (_worker != null) _worker.ReportProgress(i + 1);
				ISegment freeTranslationSegment;
				translationTier.TryGetSegment(i, out freeTranslationSegment);
				var freeTranslation = freeTranslationSegment as ITextSegment;

				yield return CreateSingleParagraphElement(segmentList[i].GetText(),
					(freeTranslation != null ? freeTranslation.GetText() : null));
			}
		}

		/// ------------------------------------------------------------------------------------
		public XElement CreateSingleParagraphElement(string transcription, string freeTranslation)
		{
			var transcriptionElement = CreateSingleWordElement(transcription);
			var phraseElement = new XElement("phrase", transcriptionElement);

			if (freeTranslation != null)
				phraseElement.Add(CreateItemElement(_wsFreeTranslationId, "gls", freeTranslation));

			return new XElement("paragraph", new XElement("phrases", phraseElement));
		}

		/// ------------------------------------------------------------------------------------
		public XElement CreateSingleWordElement(string text)
		{
			return new XElement("words", new XElement("word",
				CreateItemElement(_wsTranscriptionId, "txt", text)));
		}

		/// ------------------------------------------------------------------------------------
		public XElement CreateItemElement(string langId, string type, string text)
		{
			return new XElement("item", new XAttribute("type", type),
				new XAttribute("lang", langId), text);
		}

		/// ------------------------------------------------------------------------------------
		public static void Save(string outputFilePath, string title, ITier transcriptionTier,
			string wsTranscriptionId, string wsFreeTranslationId)
		{
			var helper = new InterlinearXmlHelper(outputFilePath, title,
				transcriptionTier, wsTranscriptionId, wsFreeTranslationId);

			var caption = LocalizationManager.GetString(
					"EventsView.Transcription.TextAnnotationEditor.ExportingToFLExInterlinear.ProgressDlg.Caption",
					"Exporting to FLEx Interlinear");

			using (var dlg = new ProgressDlg(helper, caption))
			{
				dlg.StartPosition = FormStartPosition.CenterScreen;
				dlg.ShowDialog();
			}

			UsageReporter.SendNavigationNotice("Export to FieldWorks.");
		}
	}
}
