﻿using System;
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

namespace Dropbox {
    public class Dropbox : Addon {
        public Settings settings;

        public NotifyIcon Tray;

        public bool DropboxInstalled = false;

        public string dbPath = "";
        public string dbHttp = "";
        public string imageFormat = "PNG";
        public bool useMD5 = false;
        public bool shortMD5 = false;
        public int length = 8;

        public string shortCutDragModifiers = "";
        public string shortCutDragKey = "";
        public string shortCutPasteModifiers = "";
        public string shortCutPasteKey = "";

        public bool jpegCompression = false;
        public int jpegCompressionFilesize = 1000;
        public int jpegCompressionRate = 75;

        private Bitmap bmpIcon;

        public void Initialize(NotifyIcon Tray) {
            this.Tray = Tray;
            this.DropboxInstalled = File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\Dropbox\\config.db");
            this.bmpIcon = new Icon("Addons/Dropbox/Icon.ico").ToBitmap();
            this.settings = new Settings("Addons/Dropbox/settings.txt");

            LoadSettings();
        }

        public void LoadSettings() {
            if (settings.Contains("ShortcutModifiers")) {
                // Migrate old 3.00 config file
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

            dbPath = settings.GetString("Path");
            dbHttp = settings.GetString("Http");

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

            if (dbPath != "" && dbHttp != "" && DropboxInstalled) {
                Hashtable DragItem = new Hashtable();
                DragItem.Add("Visible", true);
                DragItem.Add("Text", "Drag -> Dropbox");
                DragItem.Add("Image", this.bmpIcon);
                DragItem.Add("Action", new Action(delegate { this.Drag(new Action<DragCallback>(DragCallback)); }));
                DragItem.Add("ShortcutModifiers", this.shortCutDragModifiers);
                DragItem.Add("ShortcutKey", this.shortCutDragKey);
                ret.Add(DragItem);

                Hashtable UpItem = new Hashtable();
                UpItem.Add("Visible", Clipboard.ContainsImage() || Clipboard.ContainsFileDropList() || Clipboard.ContainsText());
                UpItem.Add("Text", "Dropbox");
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
            Tray.Icon = new Icon("Addons/Dropbox/Icon.ico");

            ImageFormat format = ImageFormat.Png;
            string formatStr = imageFormat.ToLower();

            switch (imageFormat.ToLower()) {
                case "png": format = ImageFormat.Png; break;
                case "jpg": format = ImageFormat.Jpeg; break;
                case "gif": format = ImageFormat.Gif; break;
            }

            bool result = false;
            string failReason = "";
            string filename = "";

            try {
                this.ImagePipeline(img);

                filename = this.RandomFilename(this.settings.GetInt("Length")).ToLower();
                if (this.useMD5) {
                    filename = MD5(filename + rnd.Next(1000, 9999).ToString());

                    if (this.shortMD5)
                        filename = filename.Substring(0, this.length);
                }

                if (jpegCompression) {
                    MemoryStream ms = new MemoryStream();
                    img.Save(ms, format);

                    if (ms.Length / 1000 > jpegCompressionFilesize) {
                        ms.Dispose();

                        // Set up the encoder, codec and params
                        System.Drawing.Imaging.Encoder jpegEncoder = System.Drawing.Imaging.Encoder.Compression;
                        ImageCodecInfo jpegCodec = this.GetEncoder(ImageFormat.Jpeg);
                        EncoderParameters jpegParams = new EncoderParameters();
                        jpegParams.Param[0] = new EncoderParameter(jpegEncoder, jpegCompressionRate);

                        // Now save it with the new encoder
                        filename += ".jpg";
                        img.Save(dbPath + "/" + filename, jpegCodec, jpegParams);
                    } else {
                        filename += "." + formatStr;
                        img.Save(dbPath + "/" + filename, format);
                    }
                } else {
                    filename += "." + formatStr;
                    img.Save(dbPath + "/" + filename, format);
                }

                this.Backup(dbPath + "/" + filename);

                result = true;
            } catch (Exception ex) { failReason = ex.Message; }

            if (result) {
                string url = dbHttp + filename;
                this.AddLog(url, img.Width + " x " + img.Height);
                this.SetClipboardText(url);
                Tray.ShowBalloonTip(1000, "Save success!", "Image uploaded to Dropbox and URL copied to clipboard.", ToolTipIcon.Info);
            } else
                Tray.ShowBalloonTip(1000, "Save failed!", "Something went wrong, it has to be something in your settings. Try again.\nMessage: '" + failReason + "'", ToolTipIcon.Error);

            img.Dispose();

            Tray.Icon = defIcon;
        }

        public void UploadAnimation(MemoryStream ms) {
            Icon defIcon = (Icon)Tray.Icon.Clone();
            Tray.Icon = new Icon("Addons/Dropbox/Icon.ico");

            bool result = false;
            string failReason = "";
            string filename = "";

            try {
                filename = this.RandomFilename(this.settings.GetInt("Length")).ToLower();
                if (this.useMD5) {
                    filename = MD5(filename + rnd.Next(1000, 9999).ToString());

                    if (this.shortMD5)
                        filename = filename.Substring(0, this.length);
                }

                filename += ".gif";

                FileStream fs = File.OpenWrite(dbPath + "/" + filename);
                fs.Write(ms.GetBuffer(), 0, (int)ms.Length);
                fs.Close();
                fs.Dispose();

                this.Backup(dbPath + "/" + filename);

                result = true;
            } catch (Exception ex) { failReason = ex.Message; }

            if (result) {
                string url = dbHttp + filename;
                this.AddLog(url, (ms.Length / 1000) + " kB");
                this.SetClipboardText(url);
                Tray.ShowBalloonTip(1000, "Save success!", "Animation uploaded to Dropbox and URL copied to clipboard.", ToolTipIcon.Info);
            } else
                Tray.ShowBalloonTip(1000, "Save failed!", "Something went wrong, it has to be something in your settings. Try again.\nMessage: '" + failReason + "'", ToolTipIcon.Error);

            Tray.Icon = defIcon;
        }

        public void UploadText(string Text) {
            Icon defIcon = (Icon)Tray.Icon.Clone();
            Tray.Icon = new Icon("Addons/Dropbox/Icon.ico");

            bool result = false;
            string failReason = "";
            string filename = "";

            try {
                filename = this.RandomFilename(this.settings.GetInt("Length")).ToLower();
                if (this.useMD5) {
                    filename = MD5(filename + rnd.Next(1000, 9999).ToString());

                    if (this.shortMD5)
                        filename = filename.Substring(0, this.length);
                }

                filename += ".txt";

                File.WriteAllText(dbPath + "/" + filename, Text);

                this.Backup(dbPath + "/" + filename);

                result = true;
            } catch (Exception ex) { failReason = ex.Message; }

            if (result) {
                string url = dbHttp + filename;
                this.AddLog(url, Text.Length + " characters");
                this.SetClipboardText(url);
                Tray.ShowBalloonTip(1000, "Save success!", "Text uploaded to Dropbox and URL copied to clipboard.", ToolTipIcon.Info);
            } else
                Tray.ShowBalloonTip(1000, "Save failed!", "Something went wrong, it has to be something in your settings. Try again.\nMessage: '" + failReason + "'", ToolTipIcon.Error);

            Tray.Icon = defIcon;
        }

        public void UploadFiles(StringCollection files) {
            Icon defIcon = (Icon)Tray.Icon.Clone();
            Tray.Icon = new Icon("Addons/Dropbox/Icon.ico");

            bool result = false;
            string failReason = "";
            string finalCopy = "";

            try {
                foreach (string file in files) {
                    string filename = file.Split('/', '\\').Last();

                    File.Copy(file, dbPath + "/" + filename);
                    string url = dbHttp + Uri.EscapeDataString(filename);
                    this.AddLog(url, (new FileInfo(file).Length / 1000) + " kB");
                    finalCopy += url + "\n";
                }

                result = true;
            } catch (Exception ex) { failReason = ex.Message; }

            if (result) {
                this.SetClipboardText(finalCopy.Substring(0, finalCopy.Length - 1));
                Tray.ShowBalloonTip(1000, "Save success!", "File(s) copied to your Dropbox Public folder and URL(s) copied.", ToolTipIcon.Info);
            } else
                Tray.ShowBalloonTip(1000, "Save failed!", "Something went wrong, it has to be something in your settings. Try again.\nMessage: '" + failReason + "'", ToolTipIcon.Error);

            Tray.Icon = defIcon;
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
