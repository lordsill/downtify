﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Xml;

namespace Downtify.GUI
{
    public partial class frmMain : Form
    {
        SpotifyDownloader downloader;
        public static XmlConfiguration configuration;
        public static LanguageXML lang;

        public frmMain()
        {
            InitializeComponent();

            configuration = new XmlConfiguration("config.xml");
            configuration.LoadConfigurationFile();
            downloader = new SpotifyDownloader();
            downloader.OnLoginResult += OnLoginResult;
            downloader.OnDownloadComplete += downloader_OnDownloadComplete;
            downloader.OnDownloadProgress += downloader_OnDownloadProgress;
        }

        // Very ugly, todo: move parts of this to the downloader class
        private void downloader_OnDownloadComplete(bool successfully)
        {
            var list = new object[listBoxTracks.SelectedItems.Count];
            for (int i = 1; i < listBoxTracks.SelectedItems.Count; i++)
                list[i - 1] = listBoxTracks.SelectedItems[i];

            listBoxTracks.SelectedItems.Clear();

            foreach (var track in list)
                listBoxTracks.SelectedItems.Add(track);

            if (listBoxTracks.SelectedItems.Count == 0)
            {
                listBoxTracks.SelectedItems.Clear();
                MessageBox.Show(lang.GetString("download/done"));
                EnableControls(true);
                return;
            }
            progressBar1.CurrentTrack++;
            downloader.Download(((TrackItem)listBoxTracks.SelectedItems[0]).Track);
        }

        private void downloader_OnDownloadProgress(int value)
        {
            this.Invoke((MethodInvoker)delegate
            {
                if (value > 100 || value < 0)
                    return;

                 progressBar1.Value = value;
            });
        }

        private void frmMain_Load(object sender, EventArgs e)
        {
            EnableControls(false);
        }

        private async void frmMain_Shown(object sender, EventArgs e)
        {
            System.Threading.Thread.Sleep(200);
            this.Activate();

            string username = "", password = "";
            TransferConfig();
            username = configuration.GetConfiguration("username");
            password = configuration.GetConfiguration("password");
            lang = new LanguageXML(configuration.GetConfiguration("language", "en"));

            textBoxLink.Placeholder = lang.GetString("download/paste_uri");
            progressBar1.Text = lang.GetString("download/progression");

            downloader.Login(username, password);

            if (configuration.GetConfiguration("continue_dl", "false").ToLower() == "true" && File.Exists("download.xml"))
            {
                XmlDocument doc = new XmlDocument();
                doc.Load("download.xml");
                foreach (XmlNode node in doc.SelectNodes("tracks/track"))
                    await AddDownload(node.InnerText);
            }
        }

        private void frmMain_Closing(object sender, FormClosingEventArgs e)
        {
            if (buttonDownload.Enabled == false && configuration.GetConfiguration("continue_dl", "false").ToLower() == "true")
            {
                if (File.Exists("download.xml"))
                    File.Delete("download.xml");
                List<string> tracks = new List<string>();
                foreach (TrackItem track in listBoxTracks.SelectedItems)
                    tracks.Add(SpotifySharp.Link.CreateFromTrack(track.Track, 0).AsString());
                if (tracks.Count > 0)
                {
                    XmlDocument doc = new XmlDocument();
                    XmlNode root = doc.CreateElement("tracks");
                    doc.AppendChild(root);
                    foreach (string trackLink in tracks)
                    {
                        XmlNode track = doc.CreateElement("track");
                        track.InnerText = trackLink;
                        root.AppendChild(track);
                    }
                    doc.Save("download.xml");
                }
            }
        }

        private void TransferConfig()
        {
            if(File.Exists("config.txt"))
            {
                string username = "", password = "";
                foreach(var currentLine in File.ReadAllLines("config.txt"))
                {
                    var line = currentLine.Trim();
                    if (line.StartsWith("#"))
                        continue;

                    if (line.StartsWith("username"))
                        username = line.Split('"')[1].Split('"')[0];
                    else if (line.StartsWith("password"))
                        password = line.Split('"')[1].Split('"')[0];
                }
                if (configuration.GetConfiguration("username") == "USERNAME")
                    configuration.SetConfigurationEntry("username", username);
                if (configuration.GetConfiguration("password") == "PASSWORD")
                    configuration.SetConfigurationEntry("password", password);
                configuration.SaveConfigurationFile();
                File.Delete("config.txt");
            }
        }

        private void OnLoginResult(bool isLoggedIn)
        {
            if (!isLoggedIn)
            {
                MessageBox.Show(lang.GetString("error/no_premium"), lang.GetString("title/error"));
                Application.Exit();
                return;
            }

            EnableControls(true);
        }

        private void EnableControls(bool enable)
        {
            foreach (var control in this.Controls)
                ((Control)control).Enabled = enable;
        }

        private async void textBoxLink_TextChanged(object sender, EventArgs e)
        {
            await AddDownload(textBoxLink.Text);
        }

        private void listBoxTracks_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                if (listBoxTracks.SelectedItems.Count == 0)
                    return;

                var list = new TrackItem[listBoxTracks.SelectedItems.Count];
                listBoxTracks.SelectedItems.CopyTo(list, 0);

                foreach (var track in list)
                    listBoxTracks.Items.Remove(track);
            }
            else if (e.KeyCode == Keys.A && e.Control)
            {
                var list = new TrackItem[listBoxTracks.Items.Count];
                listBoxTracks.Items.CopyTo(list, 0);

                listBoxTracks.SelectedItems.Clear();
                foreach (var track in list)
                    listBoxTracks.SelectedItems.Add(track);
            }
        }

        private void buttonDownload_Click(object sender, EventArgs e)
        {
            if (listBoxTracks.SelectedItems.Count == 0)
            {
                MessageBox.Show(lang.GetString("error/no_download_selection"), lang.GetString("title/error"));
                return;
            }

            progressBar1.TotalTracks = listBoxTracks.SelectedItems.Count;
            progressBar1.CurrentTrack = 1;
            progressBar1.ShowText = true;

            EnableControls(false);
            downloader.Download(((TrackItem)listBoxTracks.SelectedItems[0]).Track);
        }

        private async Task AddDownload(string link)
        {
            try
            {
                EnableControls(false);

                //Validate pasted URI
                if (link.Length > 0 && !link.ToLower().StartsWith("spotify:") && !link.Contains("play.spotify.com"))
                {
                    MessageBox.Show(lang.GetString("download/invalid_uri"));
                    textBoxLink.Clear();
                    return;
                }
                else if (link.Contains("play.spotify.com"))
                {
                    link = BuildSpotifyURI(link);
                }

                if (link.ToLower().Contains("playlist"))
                {
                    var playlist = await downloader.FetchPlaylist(link);
                    for (int i = 0; i < playlist.NumTracks(); i++)
                        listBoxTracks.Items.Add(new TrackItem(playlist.Track(i)));
                    textBoxLink.Clear();
                }
                else if (link.ToLower().Contains("track"))
                {
                    var track = await downloader.FetchTrack(link);
                    listBoxTracks.Items.Add(new TrackItem(track));
                    textBoxLink.Clear();
                }
                else if (link.ToLower().Contains("album"))
                {
                    var album = await downloader.FetchAlbum(link);
                    for (int i = 0; i < album.NumTracks(); i++)
                        listBoxTracks.Items.Add(new TrackItem(album.Track(i)));
                    textBoxLink.Clear();
                }
            }
            catch (NullReferenceException)
            {
            }
            finally
            {
                EnableControls(true);
            }
        }

        private string BuildSpotifyURI(string url)
        {
            string uri = url;

            Regex regex = new Regex(@"https?:\/\/play\.spotify\.com\/(user\/(?<uid>(\d.+))\/)?(?<type>playlist|album|track)\/(?<tid>.+)"); //ToDo: Optimize this RegEx

            Match match = regex.Match(url);

            if (match.Success)
            {
                string uid = match.Groups["uid"].Value != "" ? "user:" + match.Groups["uid"].Value + ":" : "";
                string type = match.Groups["type"].Value;
                string tid = match.Groups["tid"].Value;
                uri = "spotify:" + uid + type + ":" + tid;
            }
            return uri;
        }
    }
}
