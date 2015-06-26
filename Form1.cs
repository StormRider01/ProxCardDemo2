using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProxCard2
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            var x = new ReaderList();
            x.CardPresented += x_CardPresented;
            x.Refresh();

        }

        void x_CardPresented(string reader, byte[] cardData)
        {
            try
            {
                var hex = SCARD.ToHex(cardData, "");
                var bin = hex2bin(hex.Substring(hex.Length - 8));
                bin = bin.Substring(bin.Length - 26);

                var f = Convert.ToInt32(bin.Substring(1, 8),2);
                var c = Convert.ToInt32(bin.Substring(9, 16),2);

                var raw = new BitArray(cardData);
                //var x = raw.Length - 25;

                //BitArray fac = new BitArray(8);
                //BitArray car = new BitArray(16);
                //for (int i = 0; i < 8; i++) { fac[i] = raw[x + i]; }
                //for (int f = 0; f < 16; f++) { car[f] = raw[x + 8 + f]; }
                //var facility = ToNumeral(fac);
                //var cardNum = ToNumeral(car);
            }
            catch (Exception ex)
            {
                
            }

        }

        public int ToNumeral(BitArray binary)
        {
            if (binary == null)
                throw new ArgumentNullException("binary");
            if (binary.Length > 32)
                throw new ArgumentException("must be at most 32 bits long");

            var result = new int[1];
            binary.CopyTo(result, 0);
            return result[0];
        }

        public static string hex2bin(string value)
        {
            return Convert.ToString(Convert.ToInt32(value, 16), 2).PadLeft(value.Length * 4, '0');
        }
       
    }
}
