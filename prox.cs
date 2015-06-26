using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using System.Collections;
using System.Globalization;
//based on Chris's response here: http://stackoverflow.com/questions/4497671/contact-less-card-through-an-omnikey-how-to-get-uid/14939168#14939168
namespace ProxCard2
{
    public delegate void VoidDelegate();
    public delegate void CardPresented(string reader, byte[] cardData);

    public class ReaderList : IDisposable, IEnumerable<string>
    {
        public ReaderList()
        { }

        public void Dispose()
        {
            StopThread();
        }

        private Thread thread;
        private void StartThread()
        {
            if (thread != null)
                StopThread();

            thread = new Thread(Run);
            thread.IsBackground = true;
            bStopThread = false;
            thread.Start();
        }

        private void StopThread()
        {
            if (thread != null)
            {
                bStopThread = true;
                Thread.Sleep(50);
            }
            if (thread != null)
                thread.Abort();
            if (thread != null)
                thread.Join();
            thread = null;
        }

        private List<string> readerNames = new List<string>();
        private Dictionary<string, string> lastCardFound = new Dictionary<string, string>();
        public int ReaderCount
        { get { return readerNames.Count; } }

        public void Refresh()
        {
            if (thread == null)
                StartThread();
        }

        public event VoidDelegate ListChanged;
        public event CardPresented CardPresented;

        private bool bStopThread = true;
        private void Run()
        {
            IntPtr hContext = IntPtr.Zero;

            try
            {
                uint result = SCARD.EstablishContext(SCARD.SCOPE_SYSTEM, IntPtr.Zero, IntPtr.Zero, ref hContext);
                if (result != SCARD.S_SUCCESS)
                {
                    thread = null;
                    return;
                }

                uint notification_state = SCARD.STATE_UNAWARE;

                while (!bStopThread)    // loop 1 - build list, then iterate
                {
                    SCARD.ReaderState[] states = new SCARD.ReaderState[ReaderCount + 1];
                    states[0] = new SCARD.ReaderState(@"\\?PNP?\NOTIFICATION");
                    states[0].dwCurrentState = notification_state;

                    int iState = 0;
                    if (readerNames != null)
                        foreach (string s in readerNames)
                        {
                            iState++;
                            states[iState] = new SCARD.ReaderState(s);
                            states[iState].dwCurrentState = SCARD.STATE_UNAWARE;
                        }

                    while (!bStopThread)    // loop 2 - iterate over list built above
                    {
                        result = SCARD.GetStatusChange(hContext, 250, states, (uint)states.Length);
                        if (result == SCARD.E_TIMEOUT)
                            continue;
                        if (result != SCARD.S_SUCCESS)
                            break;

                        bool bReaderListChanged = false;
                        for (int i = 0; i < states.Length; i++)
                            if ((states[i].dwEventState & SCARD.STATE_CHANGED) != 0)
                                if (i == 0)
                                {
                                    // reader added or removed
                                    notification_state = states[0].dwEventState;

                                    // we want to replace the member in one step, rather than modifying it...
                                    List<string> tmp = GetReaderList(hContext, SCARD.GROUP_ALL_READERS);
                                    if (tmp == null)
                                        readerNames.Clear();
                                    else
                                        readerNames = tmp;

                                    if (ListChanged != null)
                                        ListChanged();
                                    bReaderListChanged = true;
                                }
                                else
                                {
                                    // card added or removed
                                    states[i].dwCurrentState = states[i].dwEventState;

                                    if ((states[i].dwEventState & SCARD.STATE_PRESENT) != 0)
                                    {
                                        byte[] cardData = new byte[states[i].cbATR];
                                        for (int j = 0; j < cardData.Length; j++)
                                            cardData[j] = states[i].rgbATR[j];
                                        string thisCard = SCARD.ToHex(cardData, "");
                                        string lastCard;
                                        lastCardFound.TryGetValue(states[i].szReader, out lastCard);
                                        if (thisCard != lastCard)
                                        {
                                            lastCardFound[states[i].szReader] = thisCard;
                                            if (CardPresented != null)
                                            {
                                                CardPresented(states[i].szReader, cardData);
                                            }
                                        }
                                    }
                                    else
                                        lastCardFound[states[i].szReader] = "";
                                }

                        if (bReaderListChanged)
                            break;  // break out of loop 2, and re-build our 'states' list

                    } // end loop 2
                } // end loop 1
            }
            catch (Exception ex)
            {
                //TODO: error logging
            }
            finally
            {
                if (hContext != IntPtr.Zero)
                    SCARD.ReleaseContext(hContext);
                thread = null;
            }
        }

        private List<string> GetReaderList(IntPtr hContext, string sGroup)
        {
            uint nStringLength = 0;
            uint result = SCARD.ListReaders(hContext, sGroup, null, ref nStringLength);
            if (result != SCARD.S_SUCCESS)
                return null;

            string sReaders = new string(' ', (int)nStringLength);
            result = SCARD.ListReaders(hContext, sGroup, sReaders, ref nStringLength);
            if (result != SCARD.S_SUCCESS)
                return null;
            List<string> list = new List<string>(sReaders.Split('\0'));
            for (int i = 0; i < list.Count; )
                if (list[i].Trim().Length > 0)
                    i++;
                else
                    list.RemoveAt(i);
            return list;
        }

        public IEnumerator<string> GetEnumerator()
        { return readerNames.GetEnumerator(); }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        { return readerNames.GetEnumerator(); }

    }

    public class SCARD
    {
        [DllImport("WinScard.dll", EntryPoint = "SCardEstablishContext")]
        public static extern uint EstablishContext(
            uint dwScope,
            IntPtr nNotUsed1,
            IntPtr nNotUsed2,
            ref IntPtr phContext);

        [DllImport("WinScard.dll", EntryPoint = "SCardReleaseContext")]
        public static extern uint ReleaseContext(
            IntPtr hContext);

        [DllImport("winscard.dll", EntryPoint = "SCardGetStatusChangeW", CharSet = CharSet.Unicode)]
        public static extern uint GetStatusChange(
            IntPtr hContext,
            uint dwTimeout,
            [In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)]
                SCARD.ReaderState[] rgReaderState,
            uint cReaders);

        [DllImport("winscard.dll", EntryPoint = "SCardListReadersW", CharSet = CharSet.Unicode)]
        public static extern uint ListReaders(
            IntPtr hContext,
            string groups,
            string readers,
            ref uint size);

        #region Error codes
        public const uint S_SUCCESS = 0x00000000;
        public const uint F_INTERNAL_ERROR = 0x80100001;
        public const uint E_CANCELLED = 0x80100002;
        public const uint E_INVALID_HANDLE = 0x80100003;
        public const uint E_INVALID_PARAMETER = 0x80100004;
        public const uint E_INVALID_TARGET = 0x80100005;
        public const uint E_NO_MEMORY = 0x80100006;
        public const uint F_WAITED_TOO_LONG = 0x80100007;
        public const uint E_INSUFFICIENT_BUFFER = 0x80100008;
        public const uint E_UNKNOWN_READER = 0x80100009;
        public const uint E_TIMEOUT = 0x8010000A;
        public const uint E_SHARING_VIOLATION = 0x8010000B;
        public const uint E_NO_SMARTCARD = 0x8010000C;
        public const uint E_UNKNOWN_CARD = 0x8010000D;
        public const uint E_CANT_DISPOSE = 0x8010000E;
        public const uint E_PROTO_MISMATCH = 0x8010000F;
        public const uint E_NOT_READY = 0x80100010;
        public const uint E_INVALID_VALUE = 0x80100011;
        public const uint E_SYSTEM_CANCELLED = 0x80100012;
        public const uint F_COMM_ERROR = 0x80100013;
        public const uint F_UNKNOWN_ERROR = 0x80100014;
        public const uint E_INVALID_ATR = 0x80100015;
        public const uint E_NOT_TRANSACTED = 0x80100016;
        public const uint E_READER_UNAVAILABLE = 0x80100017;
        public const uint P_SHUTDOWN = 0x80100018;
        public const uint E_PCI_TOO_SMALL = 0x80100019;
        public const uint E_READER_UNSUPPORTED = 0x8010001A;
        public const uint E_DUPLICATE_READER = 0x8010001B;
        public const uint E_CARD_UNSUPPORTED = 0x8010001C;
        public const uint E_NO_SERVICE = 0x8010001D;
        public const uint E_SERVICE_STOPPED = 0x8010001E;
        public const uint E_UNEXPECTED = 0x8010001F;
        public const uint E_ICC_INSTALLATION = 0x80100020;
        public const uint E_ICC_CREATEORDER = 0x80100021;
        public const uint E_UNSUPPORTED_FEATURE = 0x80100022;
        public const uint E_DIR_NOT_FOUND = 0x80100023;
        public const uint E_FILE_NOT_FOUND = 0x80100024;
        public const uint E_NO_DIR = 0x80100025;
        public const uint E_NO_FILE = 0x80100026;
        public const uint E_NO_ACCESS = 0x80100027;
        public const uint E_WRITE_TOO_MANY = 0x80100028;
        public const uint E_BAD_SEEK = 0x80100029;
        public const uint E_INVALID_CHV = 0x8010002A;
        public const uint E_UNKNOWN_RES_MNG = 0x8010002B;
        public const uint E_NO_SUCH_CERTIFICATE = 0x8010002C;
        public const uint E_CERTIFICATE_UNAVAILABLE = 0x8010002D;
        public const uint E_NO_READERS_AVAILABLE = 0x8010002E;
        public const uint E_COMM_DATA_LOST = 0x8010002F;
        public const uint E_NO_KEY_CONTAINER = 0x80100030;
        public const uint W_UNSUPPORTED_CARD = 0x80100065;
        public const uint W_UNRESPONSIVE_CARD = 0x80100066;
        public const uint W_UNPOWERED_CARD = 0x80100067;
        public const uint W_RESET_CARD = 0x80100068;
        public const uint W_REMOVED_CARD = 0x80100069;
        public const uint W_SECURITY_VIOLATION = 0x8010006A;
        public const uint W_WRONG_CHV = 0x8010006B;
        public const uint W_CHV_BLOCKED = 0x8010006C;
        public const uint W_EOF = 0x8010006D;
        public const uint W_CANCELLED_BY_USER = 0x8010006E;
        public const uint W_CARD_NOT_AUTHENTICATED = 0x8010006F;
        #endregion

        public const uint SCOPE_USER = 0;
        public const uint SCOPE_TERMINAL = 1;
        public const uint SCOPE_SYSTEM = 2;

        public const string GROUP_ALL_READERS = "SCard$AllReaders\0\0";
        public const string GROUP_DEFAULT_READERS = "SCard$DefaultReaders\0\0";
        public const string GROUP_LOCAL_READERS = "SCard$LocalReaders\0\0";
        public const string GROUP_SYSTEM_READERS = "SCard$SystemReaders\0\0";

        public const uint STATE_UNAWARE = 0x00000000;
        public const uint STATE_IGNORE = 0x00000001;
        public const uint STATE_CHANGED = 0x00000002;
        public const uint STATE_UNKNOWN = 0x00000004;
        public const uint STATE_UNAVAILABLE = 0x00000008;
        public const uint STATE_EMPTY = 0x00000010;
        public const uint STATE_PRESENT = 0x00000020;
        public const uint STATE_ATRMATCH = 0x00000040;
        public const uint STATE_EXCLUSIVE = 0x00000080;
        public const uint STATE_INUSE = 0x00000100;
        public const uint STATE_MUTE = 0x00000200;
        public const uint STATE_UNPOWERED = 0x00000400;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct ReaderState
        {
            public ReaderState(string sName)
            {
                szReader = sName;
                pvUserData = IntPtr.Zero;
                dwCurrentState = 0;
                dwEventState = 0;
                cbATR = 0;
                rgbATR = null;
            }

            internal string szReader;
            internal IntPtr pvUserData;
            internal uint dwCurrentState;
            internal uint dwEventState;
            internal uint cbATR;    // count of bytes in rgbATR
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x24, ArraySubType = UnmanagedType.U1)]
            internal byte[] rgbATR;
        }

        public static string ToHex(byte[] ab, string sDelim)
        {
            if (ab == null) return "<NULL>";
            return ToHex(ab, 0, ab.Length, sDelim);
        }

        public static string ToHex(byte[] ab, int offset, int len, string sDelim)
        {
            if (ab == null) return "<NULL>";
            StringBuilder sb = new StringBuilder();
            len = Math.Min(offset + len, ab.Length);
            for (int i = offset; i < len; i++)
                sb.Append(String.Format("{0:x02}", ab[i]).ToUpper() + sDelim);
            return sb.ToString();
        }

        
      

    }

    public static class extensions {
         public static string ToBitString(this BitArray bits)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < bits.Count; i++)
            {
                char c = bits[i] ? '1' : '0';
                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}