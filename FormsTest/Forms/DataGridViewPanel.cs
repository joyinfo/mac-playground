using System.Windows.Forms;

namespace FormsTest
{
    /// <summary>
    /// </summary>
    public partial class DataGridViewPanel : UserControl
    {
        /// <summary>
        /// </summary>
        public DataGridViewPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Gets the data grid 
        /// </summary>
        public DataGridView DataGrid
        {
            get
            {
                return dataGridView;
            }
        }

    }
}
