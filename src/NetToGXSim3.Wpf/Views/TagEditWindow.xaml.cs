using System;
using System.Windows;
using System.Windows.Controls;
using NetToGXSim3.Core;

namespace NetToGXSim3.Wpf.Views
{
    public partial class TagEditWindow : Window
    {
        public OpcTagItem TagItem { get; private set; }

        public TagEditWindow(OpcTagItem? existingTag = null)
        {
            InitializeComponent();

            if (existingTag != null)
            {
                TagItem = existingTag.Clone();
                TxtHeaderTitle.Text = "EDIT OPC TAG";
                TxtTagName.Text = TagItem.TagName;
                TxtAddress.Text = TagItem.Address;
                TxtDescription.Text = TagItem.Description;
                ChkActive.IsChecked = TagItem.IsActive;

                foreach (ComboBoxItem item in CmbDataType.Items)
                {
                    string itemContent = item.Content.ToString()!;
                    if (itemContent.Equals(TagItem.DataType.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        CmbDataType.SelectedItem = item;
                        break;
                    }
                }
            }
            else
            {
                TagItem = new OpcTagItem();
                TxtHeaderTitle.Text = "ADD NEW OPC TAG";
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string name = "";
            if (TxtTagName.Text != null)
            {
                name = TxtTagName.Text.Trim();
            }

            string addr = "";
            if (TxtAddress.Text != null)
            {
                addr = TxtAddress.Text.Trim();
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Tag Name cannot be empty!", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtTagName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(addr))
            {
                MessageBox.Show("Mitsubishi PLC address cannot be empty!", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtAddress.Focus();
                return;
            }

            if (!OpcDaServerEngine.TryParseMitsubishiAddress(addr, out _, out _, out _))
            {
                MessageBox.Show(
                    $"Address '{addr}' is not recognized as a valid Mitsubishi FX5U / iQ-R address!\n\n" +
                    "Please enter a valid Mitsubishi device address, for example:\n" +
                    " • X0 - X17 (Octal inputs, digits 0-7)\n" +
                    " • Y0 - Y17 (Octal outputs, digits 0-7)\n" +
                    " • M0 - M7679, L0 - L7679, SM0 - SM255\n" +
                    " • D0 - D7999, R0 - R32767, ZR0 - ZR32767\n" +
                    " • Bit of Word: D0.0 - D0.15, R0.0 - R0.15\n" +
                    " • W0 - W1FFF (Hexadecimal link registers)",
                    "Invalid Mitsubishi Address",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                TxtAddress.Focus();
                return;
            }

            TagItem.TagName = name;
            TagItem.Address = addr;
            
            if (TxtDescription.Text != null)
            {
                TagItem.Description = TxtDescription.Text.Trim();
            }
            else
            {
                TagItem.Description = "";
            }

            if (ChkActive.IsChecked.HasValue)
            {
                TagItem.IsActive = ChkActive.IsChecked.Value;
            }
            else
            {
                TagItem.IsActive = true;
            }

            string selectedDt = "Bool";
            var selectedItem = CmbDataType.SelectedItem as ComboBoxItem;
            if (selectedItem != null && selectedItem.Content != null)
            {
                selectedDt = selectedItem.Content.ToString()!;
            }

            if (Enum.TryParse(selectedDt, true, out OpcDataType dt))
            {
                TagItem.DataType = dt;
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
