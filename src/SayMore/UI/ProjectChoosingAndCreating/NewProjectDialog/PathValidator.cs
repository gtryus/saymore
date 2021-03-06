using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SayMore.UI.ProjectChoosingAndCreating.NewProjectDialog
{
	/// ----------------------------------------------------------------------------------------
	public class PathValidator
	{
		private readonly Control _ctrlMessage;
		private readonly ToolTip _tooltip;
		private string _message;
		private Color _foreColor;

		public string InvalidMessage { get; set; }

		/// ------------------------------------------------------------------------------------
		public PathValidator()
		{
		}

		/// ------------------------------------------------------------------------------------
		public PathValidator(Control ctrlMessage, ToolTip tooltip)
		{
			if (ctrlMessage == null)
				return;

			_ctrlMessage = ctrlMessage;
			_ctrlMessage.Text = string.Empty;
			_tooltip = tooltip;

			_ctrlMessage.Paint += HandleMessageControlPaint;
			_ctrlMessage.Disposed += HandleMessageControlDisposed;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Validates the path entry.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		public bool IsPathValid(string basePath, string newFolderName, string validMsg)
		{
			return IsPathValid(basePath, newFolderName, validMsg, null);
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Validates the path entry.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		public bool IsPathValid(string basePath, string newFolderName, string validMsg, string invalidMsg)
		{
			if (_tooltip != null)
				_tooltip.SetToolTip(_ctrlMessage, null);

			if (invalidMsg != null)
				InvalidMessage = invalidMsg;

			bool isValid = true;

			if (!PathOK(basePath, newFolderName))
			{
				newFolderName = newFolderName ?? string.Empty;
				_message = (newFolderName.Length > 0 ? InvalidMessage : string.Empty);
				_foreColor = Color.DarkRed;
				isValid = false;
			}
			else
			{
				var newPath = Path.Combine(basePath, newFolderName);
				if (_tooltip != null)
					_tooltip.SetToolTip(_ctrlMessage, newPath);
				string[] dirs = newPath.Split(Path.DirectorySeparatorChar);

				if (dirs.Length > 3) // Don't display leading portion of path if more than three folders deep
					newPath = Path.Combine(dirs[dirs.Length - 3], dirs[dirs.Length - 2], dirs[dirs.Length - 1]);
				_message = validMsg == null ? string.Empty : string.Format(validMsg, newPath);
				_foreColor = Color.DarkSlateGray;
			}

			if (_ctrlMessage != null)
				_ctrlMessage.Invalidate();

			return isValid;
		}

		/// ------------------------------------------------------------------------------------
		private static bool PathOK(string basePath, string relativePath)
		{
			if (basePath == null || basePath.Trim().Length < 1)
				return false;

			if (relativePath == null || relativePath.Trim().Length < 1)
				return false;

			if (basePath.IndexOfAny(Path.GetInvalidPathChars()) > -1)
				return false;

			if (relativePath.IndexOfAny(Path.GetInvalidFileNameChars()) > -1)
				return false;

			var path = Path.Combine(basePath, relativePath);
			return (!Directory.Exists(path) && !File.Exists(path));
		}

		/// ------------------------------------------------------------------------------------
		private void HandleMessageControlDisposed(object sender, System.EventArgs e)
		{
			_ctrlMessage.Paint -= HandleMessageControlPaint;
			_ctrlMessage.Disposed -= HandleMessageControlDisposed;
		}

		/// ------------------------------------------------------------------------------------
		private void HandleMessageControlPaint(object sender, PaintEventArgs e)
		{
			var formatFlags = TextFormatFlags.PathEllipsis;
			var label = _ctrlMessage as Label;
			if (label != null && label.TextAlign == ContentAlignment.MiddleLeft)
				formatFlags |= TextFormatFlags.VerticalCenter;
			TextRenderer.DrawText(e.Graphics, _message, _ctrlMessage.Font,
				_ctrlMessage.ClientRectangle, _foreColor, formatFlags);
		}
	}
}