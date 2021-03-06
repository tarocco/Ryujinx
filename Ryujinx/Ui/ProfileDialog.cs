﻿using Gtk;
using System;
using System.Reflection;

using GUI = Gtk.Builder.ObjectAttribute;

namespace Ryujinx.Ui
{
    public class ProfileDialog : Dialog
    {
        public string FileName { get; private set; }

#pragma warning disable CS0649
#pragma warning disable IDE0044
        [GUI] Entry _profileEntry;
        [GUI] Label _errorMessage;
#pragma warning restore CS0649
#pragma warning restore IDE0044

        public ProfileDialog() : this(new Builder("Ryujinx.Ui.ProfileDialog.glade")) { }

        private ProfileDialog(Builder builder) : base(builder.GetObject("_profileDialog").Handle)
        {
            builder.Autoconnect(this);

            Icon = new Gdk.Pixbuf(Assembly.GetExecutingAssembly(), "Ryujinx.Ui.assets.Icon.png");
        }

        private void OkToggle_Activated(object sender, EventArgs args)
        {
            ((ToggleButton)sender).SetStateFlags(0, true);

            bool validFileName = true;

            foreach (char invalidChar in System.IO.Path.GetInvalidFileNameChars())
            {
                if (_profileEntry.Text.Contains(invalidChar))
                {
                    validFileName = false;
                }
            }

            if (validFileName && !string.IsNullOrEmpty(_profileEntry.Text))
            {
                FileName = $"{_profileEntry.Text}.json";

                Respond(ResponseType.Ok);
            }
            else
            {
                _errorMessage.Text = "The file name contains invalid characters. Please try again.";
            }
        }

        private void CancelToggle_Activated(object sender, EventArgs args)
        {
            Respond(ResponseType.Cancel);
        }
    }
}