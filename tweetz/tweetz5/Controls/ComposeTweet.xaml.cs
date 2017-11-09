﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using tweetz5.Commands;
using tweetz5.Model;
using tweetz5.Utilities.Translate;
using Settings = tweetz5.Properties.Settings;

namespace tweetz5.Controls
{
    public sealed partial class ComposeTweet : INotifyPropertyChanged
    {
        private string _inReplyToId;
        private bool _directMessage;
        private string _directMessageRecipient;
        private string _image;
        private IInputElement _previousFocusedElement;
        private bool _isSending;
        private int _tweetLength;

        public ComposeTweet()
        {
            InitializeComponent();
            DataContext = this;
            SizeChanged += (sd, ea) => UpdateLayoutCommand.Command.Execute(null, this);
            TextBox.TextChanged += (sd, ea) => TextLength = TextBox.Text.Length;
        }

        public string Image
        {
            get => _image;
            set
            {
                if (_image == value) return;
                _image = value;
                OnPropertyChanged();
            }
        }

        public int TextLength
        {
            get => TextBox.Text.Length;
            set
            {
                if (_tweetLength == value) return;
                _tweetLength = value;
                OnPropertyChanged();
            }
        }

        public void Show(string message = "", string inReplyToId = null)
        {
            _previousFocusedElement = Keyboard.FocusedElement;
            ComposeTitle.Text = TranslationService.Instance.Translate("compose_title_tweet") as string;
            TextBox.Text = message;
            _directMessage = false;
            _directMessageRecipient = null;
            _inReplyToId = inReplyToId;
            SendButtonText.Text = TranslationService.Instance.Translate("compose_send_button_tweet") as string;
            Image = null;
            TextBox.SpellCheck.IsEnabled = Settings.Default.SpellCheck;
            Visibility = Visibility.Visible;
        }

        public void ShowDirectMessage(string screenName)
        {
            _previousFocusedElement = Keyboard.FocusedElement;
            ComposeTitle.Text = "@" + screenName;
            TextBox.Text = string.Empty;
            _directMessage = true;
            _directMessageRecipient = screenName;
            _inReplyToId = null;
            SendButtonText.Text = TranslationService.Instance.Translate("compose_send_button_message") as string;
            Image = null;
            Visibility = Visibility.Visible;
        }

        private void Hide()
        {
            TextBox.Clear();
            Visibility = Visibility.Collapsed;
            Keyboard.Focus(_previousFocusedElement);
        }

        public void Toggle()
        {
            if (IsVisible) Hide();
            else Show();
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (TextBox.IsVisible)
            {
                TextBox.Focus();
                TextBox.SelectionStart = TextBox.Text.Length;
            }
        }

        private async void OnSend(object sender, RoutedEventArgs e)
        {
            if (_isSending) return;
            _isSending = true;
            try
            {
                SendButtonText.Visibility = Visibility.Collapsed;
                SendButtonProgress.IsIndeterminate = true;
                SendButtonProgress.Visibility = Visibility.Visible;
                var text = TextBox.Text;
                string json;

                if (_directMessage)
                {
                    json = await Twitter.SendDirectMessage(text, _directMessageRecipient);
                }
                else
                {
                    json = string.IsNullOrWhiteSpace(Image)
                        ? await Twitter.UpdateStatus(text, _inReplyToId)
                        : await Twitter.UpdateStatusWithMedia(text, Image);
                }

                if (json.Contains("id_str"))
                {
                    Hide();
                    var status = Status.ParseJson("[" + json + "]");
                    UpdateStatusHomeTimelineCommand.Command.Execute(status, this);
                }
            }
            catch (Exception ex)
            {
                ComposeTitle.Text = TranslationService.Instance.Translate("compose_title_general_error") as string;
                Trace.TraceError(ex.ToString());
            }
            finally
            {
                _isSending = false;
                SendButtonText.Visibility = Visibility.Visible;
                SendButtonProgress.Visibility = Visibility.Collapsed;
                SendButtonProgress.IsIndeterminate = false;
            }
        }

        private void OnShorten(object sender, RoutedEventArgs e)
        {
            try
            {
                Shorten.IsEnabled = false;
                TextBox.Text = ShortUrl.ShortenUrls(TextBox.Text);
            }
            catch (Exception)
            {
                ComposeTitle.Text = TranslationService.Instance.Translate("compose_title_shorten_error") as string;
            }
            finally
            {
                Shorten.IsEnabled = true;
            }
        }

        private void OnPhoto(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                CheckFileExists = true,
                Filter = "Images (*.png, *.jpg, *.jpeg, *.gif)|*.png;*.jpg;*.jpeg;*.gif"
            };
            if (dialog.ShowDialog() == true)
            {
                Image = dialog.FileName;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void TextBoxOnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
            if (e.Key == Key.Return && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                OnSend(this, null);
                e.Handled = true;
            }
        }
    }
}