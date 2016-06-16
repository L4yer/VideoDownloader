﻿using System;
using System.ComponentModel;
using System.Windows.Forms;
using HtmlAgilityPack;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Microsoft.WindowsAPICodePack;
using Microsoft.WindowsAPICodePack.Taskbar;
using System.Threading;
using hdsdump;

namespace Formula1Downloader
{
    public partial class ChooseURL : Form
    {
        public ChooseURL()
        {
            InitializeComponent();
        }
        private void downloadButton_Click(object sender, EventArgs e)
        {
            downloadButton.Enabled = false;
            urlTextBox.Enabled = false;

            Uri videoURI = null;

            if (!(urlTextBox.Text.StartsWith("http://") || urlTextBox.Text.StartsWith("https://")))
                urlTextBox.Text = "https://" + urlTextBox.Text;

            bool isURLvalid = (urlTextBox.Text.StartsWith("http://www.formula1.com") || urlTextBox.Text.StartsWith("https://www.formula1.com"))
                && Uri.TryCreate(urlTextBox.Text, UriKind.Absolute, out videoURI)
                && (videoURI.Scheme == Uri.UriSchemeHttp || videoURI.Scheme == Uri.UriSchemeHttps);

            if (isURLvalid)
            {
                F1VideoTypes? videoType = getF1UriVideoType(videoURI);

                switch (videoType) {
                    case F1VideoTypes.SingleVideo:
                        Video video = getVideosFromVideoURI(videoURI).First();
                        SaveFileDialog whereToSave = new SaveFileDialog();
                        whereToSave.Filter = "FLV video (*.flv)|*.flv|All files (*.*)|*.*";
                        whereToSave.FileName = CleanFileName(video.Title);
                        if (whereToSave.ShowDialog() == DialogResult.OK)
                        {
                            // downloadProgressBar.Style = ProgressBarStyle.Marquee;
                            // TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Indeterminate);

                            BackgroundWorker downloadWorker = new BackgroundWorker();
                            downloadWorker.DoWork += delegate {
                                getVideoUsingAdobeHDS(video, whereToSave.FileName);
                            };

                            downloadWorker.RunWorkerCompleted += delegate {
                                downloadButton.Enabled = true;
                                urlTextBox.Enabled = true;
                                downloadProgressBar.Style = ProgressBarStyle.Continuous;
                                downloadProgressBar.Value = 0;
                                TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.NoProgress);
                                MessageBox.Show("Done", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                            };
                            downloadWorker.RunWorkerAsync();
                        }
                        else {
                            downloadButton.Enabled = true;
                            urlTextBox.Enabled = true;
                        }
                        break;

                    case F1VideoTypes.H5AndVideo:
                        List<Video> videosToDownload = null;
                        BackgroundWorker getManifests = new BackgroundWorker();
                        getManifests.DoWork += delegate {
                            videosToDownload = getVideosFromVideoURI(videoURI);
                        };
                        getManifests.RunWorkerCompleted += delegate {
                            if (videosToDownload.Count < 1) {
                                downloadButton.Enabled = true;
                                urlTextBox.Enabled = true;
                                return;
                            }
                            FolderBrowserDialog saveToDirectory = new FolderBrowserDialog();
                            if (saveToDirectory.ShowDialog() == DialogResult.OK)
                            {
                                int nowDownloadingVideo = 0;
                                // downloadProgressBar.Style = ProgressBarStyle.Marquee;
                                // TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Indeterminate);

                                BackgroundWorker downloadWorker = new BackgroundWorker();
                                downloadWorker.DoWork += delegate {
                                    foreach (Video vid in videosToDownload)
                                    {
                                        nowDownloadingVideo++;
                                        videoCountLabel.Invoke(new Action(() => {
                                            videoCountLabel.Visible = true;
                                            videoCountLabel.Text = "Now downloading video " + nowDownloadingVideo + " out of " + videosToDownload.Count;
                                        }));
                                        string saveFilePath = Path.Combine(saveToDirectory.SelectedPath, CleanFileName(vid.Title)) + ".flv";
                                        getVideoUsingAdobeHDS(vid, saveFilePath);
                                    }
                                };
                                downloadWorker.RunWorkerCompleted += delegate {
                                    downloadButton.Enabled = true;
                                    urlTextBox.Enabled = true;
                                    videoCountLabel.Visible = false;
                                    downloadProgressBar.Style = ProgressBarStyle.Continuous;
                                    downloadProgressBar.Value = 0;
                                    TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.NoProgress);
                                    MessageBox.Show("Done", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                                };
                                downloadWorker.RunWorkerAsync();
                            }
                            else {
                                downloadButton.Enabled = true;
                                urlTextBox.Enabled = true;
                            }
                        };
                        getManifests.RunWorkerAsync();

                        break;

                    default:
                        MessageBox.Show("Error obtaning video info from URL", ActiveForm.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        downloadButton.Enabled = true;
                        urlTextBox.Enabled = true;
                        break;
                }
            }
            else {
                MessageBox.Show("That doesn't look like a valid URL", ActiveForm.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                downloadButton.Enabled = true;
                urlTextBox.Enabled = true;
            }
        }
        private List<Video> getVideosFromVideoURI(Uri videoURI) {
            List<Video> vids = new List<Video>();

            HtmlWeb web = new HtmlWeb();
            HtmlAgilityPack.HtmlDocument htmlDoc = web.Load(videoURI.AbsoluteUri);

            HtmlNodeCollection mdNodeCol = htmlDoc.DocumentNode.SelectNodes("//div[@data-videoid]");

            Dictionary<string, string> manifests = new Dictionary<string, string>();

            if (mdNodeCol != null)
            {
                if (mdNodeCol.Count > 1)
                {
                    List<Video> availableVideos = new List<Video>();

                    foreach (HtmlNode mdnode in mdNodeCol)
                    {
                        HtmlAttribute desc = mdnode.Attributes["data-videoid"];
                        
                        HtmlNodeCollection titleNodes = mdnode.ParentNode.PreviousSibling.PreviousSibling.SelectNodes("h5");

                        // Some vids use h4

                        if (titleNodes == null)
                        {
                            titleNodes = mdnode.ParentNode.PreviousSibling.PreviousSibling.SelectNodes("h4");
                        }

                        string videoId = desc.Value;
                        string videoTitle = "";

                        foreach (HtmlNode titleNode in titleNodes)
                        {
                            string text = titleNode.InnerText.Trim();
                            if (text != "")
                            {
                                videoTitle = text;
                            }
                        }

                        availableVideos.Add(new Video(videoTitle, videoId, getF4MManifestURLForVideoId(videoId)));
                    }

                    List<string> vidTitles = new List<string>();

                    foreach (Video v in availableVideos)
                    {
                        vidTitles.Add(v.Title);
                    }

                    ChooseVideos cv = new ChooseVideos(vidTitles.ToArray());
                    AutoResetEvent autoEvent = new AutoResetEvent(false);

                    this.Invoke((MethodInvoker)delegate ()
                    {
                        cv.Show();
                        cv.FormClosed += delegate (object cv_close_sender, FormClosedEventArgs cv_close_e)
                         {
                             string[] selectedVideos = cv.CheckedItems.Cast<string>().ToArray();
                             foreach (string s in selectedVideos)
                             {
                                 foreach (Video v in availableVideos)
                                 {
                                     if (v.Title == s)
                                     {
                                         // manifests.Add(k.Key, getF4MManifestURLForVideoId(k.Value));
                                         vids.Add(v);
                                     }
                                 }
                             }
                             autoEvent.Set();
                         };
                    });
                    autoEvent.WaitOne();
                }
                else {
                    HtmlNode mdnode = mdNodeCol.First();
                    HtmlAttribute desc = mdnode.Attributes["data-videoid"];
                    string videoId = desc.Value;
                    HtmlNode titlenode = htmlDoc.DocumentNode.SelectSingleNode("//h1");
                    string videoTitle = titlenode.InnerText;

                    vids.Add(new Video(videoTitle, videoId, getF4MManifestURLForVideoId(videoId)));
                }
            }
            else {
                MessageBox.Show("Error obtaining F4M manifest URL", ActiveForm.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            return vids;
        }

        private static string getF4MManifestURLForVideoId(string videoId) {
            return string.Format("http://f1.pc.cdn.bitgravity.com/{0}/{0}_1.f4m", videoId);
        }

        private static F1VideoTypes? getF1UriVideoType(Uri videoURI)
        {
            HtmlWeb web = new HtmlWeb();
            HtmlAgilityPack.HtmlDocument htmlDoc = web.Load(videoURI.AbsoluteUri);

            HtmlNodeCollection mdNodeCol = htmlDoc.DocumentNode.SelectNodes("//div[@data-videoid]");

            if (mdNodeCol != null)
            {
                if (mdNodeCol.Count > 1)
                {
                    return F1VideoTypes.H5AndVideo;
                }
                else {
                    return F1VideoTypes.SingleVideo;
                }
            }

            return null;
        }

        private enum F1VideoTypes {
            SingleVideo,
            H5AndVideo,
        }

        private void getVideoUsingAdobeHDS(Video video, string outFile)
        {
            F4F f4f = new F4F();
            f4f.quality = "3600";
            f4f.outPath = outFile;
            f4f.DownloadFragments(video.ManifestUrl, this.downloadProgressBar);
        }

        private void creditsAndStuff_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://github.com/darxmorph");
        }

        private static string CleanFileName(string fileName)
        {
            return Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c.ToString(), string.Empty));
        }
    }
}
