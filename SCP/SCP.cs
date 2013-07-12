using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Collections;
using System.Net;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Specialized;
using AddonHelper;

using System.CodeDom.Compiler;
using Tamir.SharpSsh;

namespace SCP {
    public class SCP : Addon {
        public Settings settings;

        public NotifyIcon Tray;

        public string scpHost = "";
        public string scpUsername = "";
        public string scpPassword = "";
        public string ftpHttp = "";
        public string imageFormat = "PNG";
        public bool useMD5 = false;
        public bool shortMD5 = false;
        public int length = 8;

        public bool jpegCompression = false;
        public int jpegCompressionFilesize = 1000;
        public int jpegCompressionRate = 75;

        public string shortCutDragModifiers = "";
        public string shortCutDragKey = "";
        public string shortCutPasteModifiers = "";
        public string shortCutPasteKey = "";

        private Bitmap bmpIcon;

        public void Initialize(NotifyIcon Tray) {
            this.Tray = Tray;
            this.settings = new Settings("Addons/SCP/settings.txt");
            this.bmpIcon = new Icon("Addons/SCP/Icon.ico").ToBitmap();

            LoadSettings();
        }

        public void LoadSettings() {
            if (settings.Contains("ShortcutModifiers")) {
                // Migrate old 3.00 config file
                settings.SetBool("Binary", true);

                settings.SetString("ShortcutDragModifiers", settings.GetString("ShortcutModifiers"));
                settings.SetString("ShortcutDragKey", settings.GetString("ShortcutKey"));
                settings.SetString("ShortcutPasteModifiers", "");
                settings.SetString("ShortcutPasteKey", "");

                settings.Delete("ShortcutModifiers");
                settings.Delete("ShortcutKey");

                settings.Save();
            }

            if (!settings.Contains("JpegCompression")) {
                // Migrate old 3.10 config file
                settings.SetBool("JpegCompression", false);
                settings.SetInt("JpegCompressionFilesize", 1000);
                settings.SetInt("JpegCompressionRate", 75);

                settings.Save();
            }

            scpHost = settings.GetString("Host");
            scpUsername = settings.GetString("Username");
            scpPassword = base64Decode(settings.GetString("Password"));
            ftpHttp = settings.GetString("Http");

            imageFormat = settings.GetString("Format");

            useMD5 = settings.GetBool("UseMD5");
            shortMD5 = settings.GetBool("ShortMD5");

            length = settings.GetInt("Length");

            jpegCompression = settings.GetBool("JpegCompression");
            jpegCompressionFilesize = settings.GetInt("JpegCompressionFilesize");
            jpegCompressionRate = settings.GetInt("JpegCompressionRate");

            shortCutDragModifiers = settings.GetString("ShortcutDragModifiers");
            shortCutDragKey = settings.GetString("ShortcutDragKey");
            shortCutPasteModifiers = settings.GetString("ShortcutPasteModifiers");
            shortCutPasteKey = settings.GetString("ShortcutPasteKey");
        }

        public Hashtable[] Menu() {
            List<Hashtable> ret = new List<Hashtable>();

            if (scpHost != "" && scpUsername != "" && ftpHttp != "") {
                Hashtable DragItem = new Hashtable();
                DragItem.Add("Visible", true);
                DragItem.Add("Text", "Drag -> SCP");
                DragItem.Add("Image", this.bmpIcon);
                DragItem.Add("Action", new Action(delegate { this.Drag(new Action<DragCallback>(DragCallback)); }));
                DragItem.Add("ShortcutModifiers", this.shortCutDragModifiers);
                DragItem.Add("ShortcutKey", this.shortCutDragKey);
                ret.Add(DragItem);

                Hashtable UpItem = new Hashtable();
                UpItem.Add("Visible", Clipboard.ContainsImage() || Clipboard.ContainsFileDropList() || Clipboard.ContainsText());
                UpItem.Add("Text", "SCP");
                UpItem.Add("Image", this.bmpIcon);
                UpItem.Add("Action", new Action(Upload));
                UpItem.Add("ShortcutModifiers", this.shortCutPasteModifiers);
                UpItem.Add("ShortcutKey", this.shortCutPasteKey);
                ret.Add(UpItem);
            }

            return ret.ToArray();
        }

        public void Settings() {
            new FormSettings(this).ShowDialog();
        }

        public void DragCallback(DragCallback callback) {
            switch (callback.Type) {
                case DragCallbackType.Image:
                    UploadImage(callback.Image);
                    break;

                case DragCallbackType.Animation:
                    UploadAnimation(callback.Animation);
                    break;
            }
        }

        public void UploadImage(Image img) {
            Icon defIcon = (Icon)Tray.Icon.Clone();
            Tray.Icon = new Icon("Addons/SCP/Icon.ico");

            MemoryStream ms = new MemoryStream();

            ImageFormat format = ImageFormat.Png;
            string formatStr = imageFormat.ToLower();

            switch (formatStr) {
                case "png": format = ImageFormat.Png; break;
                case "jpg": format = ImageFormat.Jpeg; break;
                case "gif": format = ImageFormat.Gif; break;
            }

            this.ImagePipeline(img);

            img.Save(ms, format);

            if (jpegCompression) {
                if (ms.Length / 1000 > jpegCompressionFilesize) {
                    ms.Dispose();
                    ms = new MemoryStream();

                    // Set up the encoder, codec and params
                    System.Drawing.Imaging.Encoder jpegEncoder = System.Drawing.Imaging.Encoder.Compression;
                    ImageCodecInfo jpegCodec = this.GetEncoder(ImageFormat.Jpeg);
                    EncoderParameters jpegParams = new EncoderParameters();
                    jpegParams.Param[0] = new EncoderParameter(jpegEncoder, jpegCompressionRate);

                    // Now save it with the new encoder
                    img.Save(ms, jpegCodec, jpegParams);

                    // And make sure the filename gets set correctly
                    formatStr = "jpg";
                }
            }

            bool result = false;
            string failReason = "";
            string filename = "";

            bool canceled = false;
            try {
                filename = this.RandomFilename(this.settings.GetInt("Length"));
                if (this.useMD5) {
                    filename = MD5(filename + rnd.Next(1000, 9999).ToString());

                    if (this.shortMD5)
                        filename = filename.Substring(0, this.length);
                }

                filename += "." + formatStr;

                this.Backup(ms.GetBuffer(), filename);
                canceled = !UploadToSCP(ms, filename);

                result = true;
            } catch (Exception ex) { failReason = ex.Message; }

            if (!canceled) {
                if (result) {
                    string url = ftpHttp + filename;
                    this.AddLog(url, img.Width + " x " + img.Height);
                    this.SetClipboardText(url);
                    Tray.ShowBalloonTip(1000, "Upload success!", "Image uploaded to SCP and URL copied to clipboard.", ToolTipIcon.Info);
                } else {
                    this.ProgressBar.Done();
                    Tray.ShowBalloonTip(1000, "Upload failed!", "Something went wrong, probably on your SCP server. Try again.\nMessage: '" + failReason + "'", ToolTipIcon.Error);
                }
            }

            img.Dispose();

            Tray.Icon = defIcon;
        }

        public void UploadAnimation(MemoryStream ms) {
            Icon defIcon = (Icon)Tray.Icon.Clone();
            Tray.Icon = new Icon("Addons/SCP/Icon.ico");

            bool result = false;
            string failReason = "";
            string filename = "";

            bool canceled = false;
            try {
                filename = this.RandomFilename(this.settings.GetInt("Length"));
                if (this.useMD5) {
                    filename = MD5(filename + rnd.Next(1000, 9999).ToString());

                    if (this.shortMD5)
                        filename = filename.Substring(0, this.length);
                }

                filename += ".gif";

                canceled = !UploadToSCP(ms, filename);

                this.Backup(ms.GetBuffer(), filename);

                result = true;
            } catch (Exception ex) { failReason = ex.Message; }

            if (!canceled) {
                if (result) {
                    string url = ftpHttp + filename;
                    this.AddLog(url, (ms.Length / 1000) + " kB");
                    this.SetClipboardText(url);
                    Tray.ShowBalloonTip(1000, "Upload success!", "Animation uploaded to SCP and URL copied to clipboard.", ToolTipIcon.Info);
                } else {
                    this.ProgressBar.Done();
                    Tray.ShowBalloonTip(1000, "Upload failed!", "Something went wrong, probably on your SCP server. Try again.\nMessage: '" + failReason + "'", ToolTipIcon.Error);
                }
            }

            Tray.Icon = defIcon;
        }

        public void UploadText(string Text) {
            Icon defIcon = (Icon)Tray.Icon.Clone();
            Tray.Icon = new Icon("Addons/SCP/Icon.ico");

            bool result = false;
            string failReason = "";
            string filename = "";

            byte[] textData = Encoding.UTF8.GetBytes(Text);

            bool canceled = false;
            try {
                filename = this.RandomFilename(this.settings.GetInt("Length"));
                if (this.useMD5) {
                    filename = MD5(filename + rnd.Next(1000, 9999).ToString());

                    if (this.shortMD5)
                        filename = filename.Substring(0, this.length);
                }

                filename += ".txt";

                canceled = !UploadToSCP(new MemoryStream(textData), filename);

                this.Backup(textData, filename);

                result = true;
            } catch (Exception ex) { failReason = ex.Message; }

            if (!canceled) {
                if (result) {
                    string url = ftpHttp + filename;
                    this.AddLog(url, Text.Length + " characters");
                    this.SetClipboardText(url);
                    Tray.ShowBalloonTip(1000, "Upload success!", "Text uploaded to SCP and URL copied to clipboard.", ToolTipIcon.Info);
                } else {
                    this.ProgressBar.Done();
                    Tray.ShowBalloonTip(1000, "Upload failed!", "Something went wrong, probably on your SCP server. Try again.\nMessage: '" + failReason + "'", ToolTipIcon.Error);
                }
            }

            Tray.Icon = defIcon;
        }

        public void UploadFiles(StringCollection files) {
            Icon defIcon = (Icon)Tray.Icon.Clone();
            Tray.Icon = new Icon("Addons/SCP/Icon.ico");

            bool result = false;
            string failReason = "";
            string finalCopy = "";

            bool canceled = false;

            try {
                foreach (string file in files) {
                    string filename = file.Split('/', '\\').Last();

                    canceled = !UploadToSCP(new MemoryStream(File.ReadAllBytes(file)), filename);
                    if (canceled)
                        break;

                    string url = ftpHttp + Uri.EscapeDataString(filename);
                    this.AddLog(url, (new FileInfo(file).Length / 1000) + " kB");

                    finalCopy += url + "\n";
                }

                result = true;
            } catch (Exception ex) { failReason = ex.Message; }

            if (!canceled) {
                if (result) {
                    this.SetClipboardText(finalCopy.Substring(0, finalCopy.Length - 1));
                    Tray.ShowBalloonTip(1000, "Upload success!", "File(s) uploaded to your SCP folder and URL(s) copied.", ToolTipIcon.Info);
                } else {
                    this.ProgressBar.Done();
                    Tray.ShowBalloonTip(1000, "Upload failed!", "Something went wrong, probably on your SCP server. Try again.\nMessage: '" + failReason + "'", ToolTipIcon.Error);
                }
            }

            Tray.Icon = defIcon;
        }

        public bool UploadToSCP(MemoryStream ms, string filename) {
            this.ProgressBar.Start(filename, ms.Length); 
            const_progressBar = this.ProgressBar;
            // create a temp file and write stream to it
            using (TempFileCollection tempFile = new TempFileCollection()) {
                tempFile.AddFile(filename, false);      
                FileStream file = new FileStream(filename, FileMode.Create, System.IO.FileAccess.Write);
                ms.WriteTo(file);
                file.Close();

                // create transfer
                SshTransferProtocolBase scp = new Scp(scpHost, scpUsername, scpPassword);
                
                scp.OnTransferStart += new FileTransferEvent(sshCp_OnTransferStart);
				scp.OnTransferProgress += new FileTransferEvent(sshCp_OnTransferProgress);
				scp.OnTransferEnd += new FileTransferEvent(sshCp_OnTransferEnd);
                
                // connect
                scp.Connect();

                // transfer file
                scp.Put(filename, filename);
                
                this.ProgressBar.Done();

                scp.Close();
            }
            
            return true;
        }
        
        static cProgressBar const_progressBar;

        private static void sshCp_OnTransferStart(string src, string dst, int transferredBytes, int totalBytes, string message)
		{
            const_progressBar.Set(transferredBytes);
		}

		private static void sshCp_OnTransferProgress(string src, string dst, int transferredBytes, int totalBytes, string message)
		{
			if(const_progressBar!=null)
			{
                const_progressBar.Set(transferredBytes);
			}
		}

		private static void sshCp_OnTransferEnd(string src, string dst, int transferredBytes, int totalBytes, string message)
		{
			if(const_progressBar!=null)
			{
                const_progressBar.Set(transferredBytes);
				const_progressBar=null;
			}
		}

        public void Upload() {
            if (Clipboard.ContainsImage())
                UploadImage(Clipboard.GetImage());
            else if (Clipboard.ContainsText())
                UploadText(Clipboard.GetText());
            else if (Clipboard.ContainsFileDropList()) {
                StringCollection files = Clipboard.GetFileDropList();
                if (files.Count == 1 && (files[0].EndsWith(".png") || files[0].EndsWith(".jpg") || files[0].EndsWith(".gif")))
                    UploadImage(Image.FromFile(files[0]));
                else
                    UploadFiles(files);
            }
        }
    }
}
