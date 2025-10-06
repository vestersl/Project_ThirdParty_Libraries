using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using S7.Net.Internal;
using S7.Net.Protocol;
using S7.Net.Types;

namespace S7.Net
{
    /// <summary>
    /// Dirección de la trama
    /// </summary>
    public enum FrameDirection
    {
        /// <summary>
        /// Trama enviada al PLC
        /// </summary>
        Sent,
        
        /// <summary>
        /// Trama recibida del PLC
        /// </summary>
        Received
    }

    /// <summary>
    /// Representa una trama de comunicación con el PLC
    /// </summary>
    public class PlcFrame
    {
        /// <summary>
        /// Dirección de la trama (Sent = enviada, Received = recibida)
        /// </summary>
        public FrameDirection Direction { get; set; }
        
        /// <summary>
        /// Datos de la trama en formato hexadecimal
        /// </summary>
        public string HexData { get; set; }
        
        /// <summary>
        /// Timestamp de la trama
        /// </summary>
        public System.DateTime Timestamp { get; set; }
        
        /// <summary>
        /// Tipo de operación (Read, Write, etc.)
        /// </summary>
        public string Operation { get; set; }

        public PlcFrame(FrameDirection direction, byte[] data, string operation = "")
        {
            Direction = direction;
            HexData = BitConverter.ToString(data).Replace("-", "");
            Timestamp = System.DateTime.Now;
            Operation = operation;
        }
    }

    /// <summary>
    /// Creates an instance of S7.Net driver
    /// </summary>
    public partial class Plc : IDisposable
    {
        /// <summary>
        /// The default port for the S7 protocol.
        /// </summary>
        public const int DefaultPort = 102;

        /// <summary>
        /// The default timeout (in milliseconds) used for <see cref="P:ReadTimeout"/> and <see cref="P:WriteTimeout"/>.
        /// </summary>
        public const int DefaultTimeout = 10_000;

        private readonly TaskQueue queue = new TaskQueue();

        //TCP connection to device
        private TcpClient? tcpClient;
        private NetworkStream? _stream;

        private int readTimeout = DefaultTimeout;
        private int writeTimeout = DefaultTimeout;

        // Frame logging functionality
        private readonly List<PlcFrame> _frameHistory = new List<PlcFrame>();
        private readonly object _frameHistoryLock = new object();
        private bool _enableFrameLogging = false;
        private int _maxFrameHistorySize = 1000;

        /// <summary>
        /// IP address of the PLC
        /// </summary>
        public string IP { get; }

        /// <summary>
        /// PORT Number of the PLC, default is 102
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// The TSAP addresses used during the connection request.
        /// </summary>
        public TsapPair TsapPair { get; set; }

        /// <summary>
        /// CPU type of the PLC
        /// </summary>
        public CpuType CPU { get; }

        /// <summary>
        /// Rack of the PLC
        /// </summary>
        public Int16 Rack { get; }

        /// <summary>
        /// Slot of the CPU of the PLC
        /// </summary>
        public Int16 Slot { get; }

        /// <summary>
        /// Max PDU size this cpu supports
        /// </summary>
        public int MaxPDUSize { get; private set; }

        /// <summary>Gets or sets the amount of time that a read operation blocks waiting for data from PLC.</summary>
        public int ReadTimeout
        {
            get => readTimeout;
            set
            {
                readTimeout = value;
                if (tcpClient != null) tcpClient.ReceiveTimeout = readTimeout;
            }
        }

        /// <summary>Gets or sets the amount of time that a write operation blocks waiting for data to PLC. </summary>
        public int WriteTimeout
        {
            get => writeTimeout;
            set
            {
                writeTimeout = value;
                if (tcpClient != null) tcpClient.SendTimeout = writeTimeout;
            }
        }

        /// <summary>
        /// Habilita o deshabilita el logging de tramas
        /// </summary>
        public bool EnableFrameLogging
        {
            get => _enableFrameLogging;
            set => _enableFrameLogging = value;
        }

        /// <summary>
        /// Tamaño máximo del historial de tramas (por defecto 1000)
        /// </summary>
        public int MaxFrameHistorySize
        {
            get => _maxFrameHistorySize;
            set
            {
                _maxFrameHistorySize = value;
                TrimFrameHistory();
            }
        }

        /// <summary>
        /// Gets a value indicating whether a connection to the PLC has been established.
        /// </summary>
        public bool IsConnected => tcpClient?.Connected ?? false;

        // Constructores existentes...
        public Plc(CpuType cpu, string ip, Int16 rack, Int16 slot)
            : this(cpu, ip, DefaultPort, rack, slot)
        {
        }

        public Plc(CpuType cpu, string ip, int port, Int16 rack, Int16 slot)
            : this(ip, port, TsapPair.GetDefaultTsapPair(cpu, rack, slot))
        {
            if (!Enum.IsDefined(typeof(CpuType), cpu))
                throw new ArgumentException(
                    $"The value of argument '{nameof(cpu)}' ({cpu}) is invalid for Enum type '{typeof(CpuType).Name}'.",
                    nameof(cpu));

            CPU = cpu;
            Rack = rack;
            Slot = slot;
        }

        public Plc(string ip, TsapPair tsapPair) : this(ip, DefaultPort, tsapPair)
        {
        }

        public Plc(string ip, int port, TsapPair tsapPair)
        {
            if (string.IsNullOrEmpty(ip))
                throw new ArgumentException("IP address must valid.", nameof(ip));

            IP = ip;
            Port = port;
            MaxPDUSize = 240;
            TsapPair = tsapPair;
        }

        /// <summary>
        /// Close connection to PLC
        /// </summary>
        public void Close()
        {
            if (tcpClient != null)
            {
                if (tcpClient.Connected) tcpClient.Close();
                tcpClient = null;
            }
        }

        /// <summary>
        /// Obtiene el código hexadecimal de la última trama enviada o recibida
        /// </summary>
        /// <returns>String con el valor hexadecimal de la trama</returns>
        public string GetFrameHexCode()
        {
            lock (_frameHistoryLock)
            {
                var lastFrame = _frameHistory.LastOrDefault();
                return lastFrame?.HexData ?? string.Empty;
            }
        }

        /// <summary>
        /// Obtiene el código hexadecimal de la última trama del tipo especificado
        /// </summary>
        /// <param name="direction">Dirección de la trama (Sent o Received)</param>
        /// <returns>String con el valor hexadecimal de la trama</returns>
        public string GetFrameHexCode(FrameDirection direction)
        {
            lock (_frameHistoryLock)
            {
                var lastFrame = _frameHistory.Where(f => f.Direction == direction).LastOrDefault();
                return lastFrame?.HexData ?? string.Empty;
            }
        }

        /// <summary>
        /// Obtiene el historial completo de tramas
        /// </summary>
        /// <returns>Lista de tramas registradas</returns>
        public List<PlcFrame> GetFrameHistory()
        {
            lock (_frameHistoryLock)
            {
                return new List<PlcFrame>(_frameHistory);
            }
        }

        /// <summary>
        /// Limpia el historial de tramas
        /// </summary>
        public void ClearFrameHistory()
        {
            lock (_frameHistoryLock)
            {
                _frameHistory.Clear();
            }
        }

        /// <summary>
        /// Registra una trama en el historial
        /// </summary>
        /// <param name="direction">Dirección de la trama</param>
        /// <param name="data">Datos de la trama</param>
        /// <param name="operation">Tipo de operación</param>
        private void LogFrame(FrameDirection direction, byte[] data, string operation = "")
        {
            if (!_enableFrameLogging || data == null || data.Length == 0)
                return;

            lock (_frameHistoryLock)
            {
                _frameHistory.Add(new PlcFrame(direction, data, operation));
                TrimFrameHistory();
            }
        }

        /// <summary>
        /// Mantiene el historial dentro del límite especificado
        /// </summary>
        private void TrimFrameHistory()
        {
            lock (_frameHistoryLock)
            {
                while (_frameHistory.Count > _maxFrameHistorySize)
                {
                    _frameHistory.RemoveAt(0);
                }
            }
        }

        private void AssertPduSizeForRead(ICollection<DataItem> dataItems)
        {
            var requiredRequestSize = 19 + dataItems.Count * 12;
            if (requiredRequestSize > MaxPDUSize) throw new Exception($"Too many vars requested for read. Request size ({requiredRequestSize}) is larger than protocol limit ({MaxPDUSize}).");

            var requiredResponseSize = GetDataLength(dataItems) + dataItems.Count * 4 + 14;
            if (requiredResponseSize > MaxPDUSize) throw new Exception($"Too much data requested for read. Response size ({requiredResponseSize}) is larger than protocol limit ({MaxPDUSize}).");
        }

        private void AssertPduSizeForWrite(ICollection<DataItem> dataItems)
        {
            if (dataItems.Count * 18 + 12 > MaxPDUSize) throw new Exception("Too many vars supplied for write");

            if (GetDataLength(dataItems) + dataItems.Count * 16 + 12 > MaxPDUSize)
                throw new Exception("Too much data supplied for write");
        }

        private void ConfigureConnection()
        {
            if (tcpClient == null)
            {
                return;
            }

            tcpClient.ReceiveTimeout = ReadTimeout;
            tcpClient.SendTimeout = WriteTimeout;
        }

        private int GetDataLength(IEnumerable<DataItem> dataItems)
        {
            return dataItems.Select(di => VarTypeToByteLength(di.VarType, di.Count))
                .Sum(len => (len & 1) == 1 ? len + 1 : len);
        }

        private static void AssertReadResponse(byte[] s7Data, int dataLength)
        {
            var expectedLength = dataLength + 18;

            PlcException NotEnoughBytes() =>
                new PlcException(ErrorCode.WrongNumberReceivedBytes,
                    $"Received {s7Data.Length} bytes: '{BitConverter.ToString(s7Data)}', expected {expectedLength} bytes.")
            ;

            if (s7Data == null)
                throw new PlcException(ErrorCode.WrongNumberReceivedBytes, "No s7Data received.");

            if (s7Data.Length < 15) throw NotEnoughBytes();

            ValidateResponseCode((ReadWriteErrorCode)s7Data[14]);

            if (s7Data.Length < expectedLength) throw NotEnoughBytes();
        }

        internal static void ValidateResponseCode(ReadWriteErrorCode statusCode)
        {
            switch (statusCode)
            {
                case ReadWriteErrorCode.ObjectDoesNotExist:
                    throw new Exception("Received error from PLC: Object does not exist.");
                case ReadWriteErrorCode.DataTypeInconsistent:
                    throw new Exception("Received error from PLC: Data type inconsistent.");
                case ReadWriteErrorCode.DataTypeNotSupported:
                    throw new Exception("Received error from PLC: Data type not supported.");
                case ReadWriteErrorCode.AccessingObjectNotAllowed:
                    throw new Exception("Received error from PLC: Accessing object not allowed.");
                case ReadWriteErrorCode.AddressOutOfRange:
                    throw new Exception("Received error from PLC: Address out of range.");
                case ReadWriteErrorCode.HardwareFault:
                    throw new Exception("Received error from PLC: Hardware fault.");
                case ReadWriteErrorCode.Success:
                    break;
                default:
                    throw new Exception( $"Invalid response from PLC: statusCode={(byte)statusCode}.");
            }
        }

        private Stream GetStreamIfAvailable()
        {
            if (_stream == null)
            {
                throw new PlcException(ErrorCode.ConnectionError, "Plc is not connected");
            }

            return _stream;
        }

        #region IDisposable Support
        private bool disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Close();
                }

                disposedValue = true;
            }
        }

        void IDisposable.Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
