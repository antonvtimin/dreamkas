using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Timers;
using System.Windows.Forms;

namespace Dreamkas
{
    public class VikiPrint
    {
        #region Classes
        public class RequestDataBuilder
        {
            private MemoryStream stream = null;
            private BinaryWriter writer = null;

            public RequestDataBuilder()
            {
                this.stream = new MemoryStream();
                this.writer = new BinaryWriter(stream);
            }

            private Encoding encCP866 = null;
            private byte[] getStrBytes(string str)
            {
                if (encCP866 == null)
                    encCP866 = Encoding.GetEncoding("CP866");
                return Encoding.Convert(Encoding.Default, this.encCP866, Encoding.Default.GetBytes(str));
            }
            public RequestDataBuilder Write(string str, bool insertFS = true)
            {
                this.writer.Write(this.getStrBytes(str));
                if (insertFS)
                    this.writer.Write(FS);
                return this;
            }
            public RequestDataBuilder Write(byte[] bytes, bool insertFS = true)
            {
                this.writer.Write(bytes);
                this.writer.Write(FS);
                return this;
            }
            public RequestDataBuilder Write()
            {
                this.writer.Write(FS);
                return this;
            }
            public byte[] ToArray()
            {
                return this.stream.ToArray();
            }
            ~RequestDataBuilder()
            {
                this.writer.Close();
                this.writer = null;
                this.stream = null;
            }
        }

        #endregion Classes

        #region Const

        /// <summary>
        /// Спец команда. Проверка связи
        /// </summary>
        private const byte COMMAND_ENQ = 0x05;
        /// <summary>
        /// Спец. ответ. ККТ на связи
        /// </summary>
        private const byte COMMAND_ACK = 0x06;
        /// <summary>
        /// Спец. команда. Промотка бумаги
        /// </summary>
        private const byte COMMAND_LF = 0x0A;
        /// <summary>
        /// Стартовый бит пакета
        /// </summary>
        private const byte STX = 0x02;
        /// <summary>
        /// Завершающий бит пакета
        /// </summary>
        private const byte ETX = 0x03;
        /// <summary>
        /// Разделитель параметров в поле данных
        /// </summary>
        public const byte FS = 0x1C;
        /// <summary>
        /// Пароль к кассе
        /// </summary>
        private const string PASS = "PIRI";
        private const string DECIMAL_FORMAT = "0.000000000";
        /// <summary>
        /// Номер пакета, для синхронной передачи всегда одинаковый
        /// </summary>
        private const byte PACKET_ID = 0x21;

        #endregion Const

        #region Fields

        private SerialPort comPort;
        public const int DOC_TYPE_SERVICE = 1; //For print texts
        public const int DOC_TYPE_REGISTER = 2; //For fiscal registration
        public const int DOC_TYPE_RETURN = 3;
        public const int DOC_TYPE_INCOME = 4;
        public const int DOC_TYPE_OUTCOME = 5;
        public const int DOC_TYPE_BUY = 6;
        public const int DOC_TYPE_ANNULATE = 7;

        private bool isAwaitAnswer = false;
        private CommandEnum awaitedResponseType;
        private bool isGettingAnswer = false;
        private bool isContinueRead = true;
        System.Threading.Thread readThread;
        private System.Timers.Timer timerAwaitAnswer;
        private Encoding ENC_CP866 = Encoding.GetEncoding("CP866");

        #endregion Fields

        #region Constructors

        public VikiPrint(string setPortName, int setPortSpeed, string cashierName)
        {
            this.comPort = new SerialPort(setPortName, setPortSpeed, Parity.None, 8, StopBits.One);
            this.comPort.Encoding = Encoding.GetEncoding("CP866");
            this.readThread = new System.Threading.Thread(this.readCOM);
            this.timerAwaitAnswer = new System.Timers.Timer(5000);
            this.timerAwaitAnswer.Elapsed += TimerAwaitAnswer_Elapsed;

            this.OpenComPort();

            this.CashierName = cashierName;
        }

        #endregion Constructors

        public delegate void InvalidDataEventHandler(string packet, string message);
        public delegate void CommonErrorEventHandler(string error);
        public delegate void GotUnwaitedResponseEventHandler(ICashReceiptResponse response);

        public event InvalidDataEventHandler GotInvalidData;
        public event GotUnwaitedResponseEventHandler GotUnwaitedResponse;
        public event CommonErrorEventHandler CommonError;

        private void onInvalidData(string packet, string message)
        {
            if (this.GotInvalidData != null)
                this.GotInvalidData(packet, message);
        }

        private void onUnwaitedResponse(ICashReceiptResponse response)
        {
            if (response == null)
                return;
            if (this.GotUnwaitedResponse != null)
                this.GotUnwaitedResponse(response);
        }

        private void onCommonError(string error)
        {
            if (this.CommonError != null)
                this.CommonError(error);
        }

        public bool OpenComPort()
        {
            if (this.comPort.IsOpen)
                return true;
            try { this.comPort.Open(); }
            catch (Exception ex)
            {
                this.onCommonError(String.Format("Ошибка типа {0} при попытке открыть канал связи с кассой:\n{1}", ex.GetType().Name, ex.Message));
            }
            if (this.comPort.IsOpen)
                this.readThread.Start();
            return this.comPort.IsOpen;
        }

        public string CashierName { get; set; }

        private void TimerAwaitAnswer_Elapsed(object sender, ElapsedEventArgs e)
        {
            this.isAwaitAnswer = false;
            this.timerAwaitAnswer.Enabled = false;
            this.onCommonError("Не получен ответ от ККМ!");
        }

        //private void sendBytes(byte[] packet)
        //{
        //    this.comPort.Write(packet, 0, packet.Length);
        //}

        private byte[] composeRequest(CommandEnum command, string data)
        {
            int _packetLength = 1 /*STX*/ + PASS.Length /*PASS length*/ + 1 /*packet id*/ + 2 /*command*/ + (data == null ? 0 : data.Length) /*data length*/ + 1 /*ETX*/ + 2 /*CRC*/;
            byte[] _result = new byte[_packetLength];

            int _counter = 0;
            _result[_counter++] = STX;
            foreach (char _c in PASS) // пароль
                _result[_counter++] = (byte)_c;
            _result[_counter++] = PACKET_ID; // id пакета
            foreach (char _c in ((byte)command).ToString("X2")) // код команды
                _result[_counter++] = (byte)_c;
            if (data != null)
            {
                foreach (char _c in data) // данные
                    _result[_counter++] = (byte)_c;
            }
            _result[_counter++] = ETX;

            string _crcStr = this.calculateCRC(_result, 1, _packetLength - 3).ToString("X2");
            foreach (char _c in _crcStr)
                _result[_counter++] = (byte)_c;


            return _result;
        }

        private byte[] composeRequest(CommandEnum command, byte[] data)
        {
            int _packetLength = 1 /*STX*/ + PASS.Length /*PASS length*/ + 1 /*packet id*/ + 2 /*command*/ + (data == null ? 0 : data.Length) /*data length*/ + 1 /*ETX*/ + 2 /*CRC*/;
            byte[] _result = new byte[_packetLength];

            int _counter = 0;
            _result[_counter++] = STX;
            foreach (char _c in PASS) // пароль
                _result[_counter++] = (byte)_c;
            _result[_counter++] = PACKET_ID; // id пакета
            foreach (char _c in ((byte)command).ToString("X2")) // код команды
                _result[_counter++] = (byte)_c;
            if (data != null)
            {
                foreach (byte _b in data) // данные
                    _result[_counter++] = _b;
            }
            _result[_counter++] = ETX;

            string _crcStr = this.calculateCRC(_result, 1, _packetLength - 3).ToString("X2");
            foreach (char _c in _crcStr)
                _result[_counter++] = (byte)_c;


            return _result;
        }

        private ICashReceiptResponse decomposeResponse(byte[] stream)
        {
            if (stream.Length == 1)
                return null;

            int _packetLength = stream.Length;
            if (stream[0] != STX || stream[1] != PACKET_ID || stream[_packetLength - 3] != ETX)
            {
                this.onInvalidData(ENC_CP866.GetString(stream), "Неверный формат пакета данных!");
                return null;
            }
            string _crcStr = this.calculateCRC(stream, 1, _packetLength - 3).ToString("X2");
            if (_crcStr[0] != (char)stream[_packetLength - 2] || _crcStr[1] != (char)stream[_packetLength - 1])
            {
                this.onInvalidData(ENC_CP866.GetString(stream), "Контрольная сумма не верна!");
                return null;
            }

            CommandEnum _command = (CommandEnum)byte.Parse(new string(new char[] { (char)stream[2], (char)stream[3] }), System.Globalization.NumberStyles.HexNumber);
            byte _errorCode = byte.Parse(new string(new char[] { (char)stream[4], (char)stream[5] }), System.Globalization.NumberStyles.HexNumber);
            string _data = ENC_CP866.GetString(stream, 6, _packetLength - 9);
            if (_errorCode != 0)
                return new ErrorResponse(_command, _data, _errorCode);

            ICashReceiptResponse _response = null;
            switch (_command)
            {
                case CommandEnum.x00_Status_Flags:
                    _response = new x00_StatusFlagsResponse(_data);
                    break;
                case CommandEnum.x42_Add_Item:
                    _response = new x42_AddItemResponse(_data);
                    break;
                case CommandEnum.x31_Close_Document:
                    _response = new x31_CloseDocumentResponse(_data);
                    break;
                case CommandEnum.x06_ErrorInfo:
                    _response = new x06_ErrorInfoResponse(_data);
                    break;
                default:
                    _response = new UnknownResponse(_command, _data);
                    break;
            }

            return _response;
        }

        //private Encoding encCP866 = null;
        //private byte[] getStrBytes(string str)
        //{
        //    if (encCP866 == null)
        //        encCP866 = Encoding.GetEncoding("CP866");
        //    return Encoding.Convert(Encoding.Default, this.encCP866, Encoding.Default.GetBytes(str));
        //}

        ICashReceiptResponse awaitedResponse = null;

        private ICashReceiptResponse readAwaitedResponse(CommandEnum awaitedResponseType)
        {
            this.awaitedResponse = null;
            this.awaitedResponseType = awaitedResponseType;
            this.isAwaitAnswer = true;
            this.timerAwaitAnswer.Start();
            while (this.isAwaitAnswer)
                System.Threading.Thread.Sleep(100);
            ICashReceiptResponse _resp = this.awaitedResponse;
            this.awaitedResponse = null;

            return _resp;
        }

        private void readCOM()
        {
            byte[] _buffer;
            while (this.isContinueRead)
            {
                //if (!this.isAwaitAnswer)
                //{
                //    if (this.comPort.BytesToRead > 0)
                //    {
                //        _buffer = new byte[this.comPort.BytesToRead];
                //        this.comPort.Read(_buffer, 0, _buffer.Length);
                //        this.gotAnswer(_buffer, true);// this.comPort.ReadExisting(), true);
                //    }
                //    continue;
                //}
                if (this.comPort.BytesToRead == 0)
                    continue;
                this.isGettingAnswer = true;
                //MessageBox.Show("gettingAnswer start");
                if (this.isAwaitAnswer)
                    this.timerAwaitAnswer.Stop();
                MemoryStream _stream = new MemoryStream();
                BinaryWriter _writer = new BinaryWriter(_stream);
                while (this.comPort.BytesToRead > 0)
                    try
                    {
                        _buffer = new byte[this.comPort.BytesToRead];
                        this.comPort.Read(_buffer, 0, _buffer.Length);
                        _writer.Write(_buffer);
                    }
                    catch (TimeoutException) { }
                _buffer = _stream.ToArray();
                _writer.Close();

                this.isGettingAnswer = false;
                ICashReceiptResponse _response = this.decomposeResponse(_buffer);
                //MessageBox.Show("gettingAnswer end");
                if (this.isAwaitAnswer)
                {
                    if (_response == null || _response.ResponseType != this.awaitedResponseType)
                    {
                        this.onUnwaitedResponse(_response);
                        this.timerAwaitAnswer.Start();
                    }
                    else
                    {
                        this.awaitedResponse = _response;
                        this.isAwaitAnswer = false;
                    }
                }
                else
                    this.onUnwaitedResponse(_response);
            }
        }

        //private void gotAnswer(byte[] stream, bool isUnknown = false)
        //{
        //    //MessageBox.Show("gotAnswer start");
        //    ICashReceiptResponse _response = this.decomposeResponse(stream);// message);
        //    if (_response == null)
        //        return;
        //    this.onGotResponse(_response, isUnknown);
        //    //MessageBox.Show("gotAnswer end");
        //}

        private byte calculateCRC(byte[] bytes, int startPos, int count)
        {
            byte _sum = 0;
            for (int _i = 0; _i < count; _i++)
                _sum = (byte)(_sum ^ bytes[_i + startPos]);
            return _sum;
        }

        private byte calculateCRC(char[] bytes, int startPos, int count)
        {
            byte _sum = 0;
            for (int _i = 0; _i < count; _i++)
                _sum = (byte)(_sum ^ (byte)bytes[_i + startPos]);
            return _sum;
        }
        public void SendENQ()
        {
            while (this.isAwaitAnswer) ;

            this.comPort.Write(new byte[1] { COMMAND_ENQ }, 0, 1);
            this.isAwaitAnswer = true;
            this.timerAwaitAnswer.Start();
        }

        /// <summary>
        /// Промотка бумаги
        /// </summary>
        public void SendLF()
        {
            while (this.isAwaitAnswer) ;

            this.comPort.Write(new byte[1] { COMMAND_LF }, 0, 1);
        }

        private ICashReceiptResponse sendCommand(CommandEnum commandType, string data, bool isAwaitResponse)
        {
            //MessageBox.Show("sendCommand start");
            while (this.isAwaitAnswer || this.isGettingAnswer) System.Threading.Thread.Sleep(100);
            byte[] _packet = this.composeRequest(commandType, data);
            this.comPort.Write(_packet, 0, _packet.Length);
            if (isAwaitResponse)
                return this.readAwaitedResponse(commandType);
            return null;
            //MessageBox.Show("sendCommand end");
        }

        private ICashReceiptResponse sendCommand(CommandEnum commandType, byte[] data, bool isAwaitResponse)
        {
            //MessageBox.Show("sendCommand start");
            while (this.isAwaitAnswer || this.isGettingAnswer) System.Threading.Thread.Sleep(100);
            byte[] _packet = this.composeRequest(commandType, data);
            this.comPort.Write(_packet, 0, _packet.Length);
            if (isAwaitResponse)
                return this.readAwaitedResponse(commandType);
            return null;
            //MessageBox.Show("sendCommand end");
        }

        /// <summary>
        /// 0x00 Запрос флагов статуса ККТ
        /// </summary>
        public ICashReceiptResponse Send00()
        {
            return this.sendCommand(CommandEnum.x00_Status_Flags, string.Empty, true);
        }

        /// <summary>
        /// 0x06 Запрос расширенной информации об ошибках
        /// </summary>
        public ICashReceiptResponse GetErrorInfo()
        {
            /*
            Входные параметры
            (Целое число) Номер запроса.

            Номер запроса (DEC)	Наименование Запроса	Формат возвращаемых данных	Комментарии
            1	Вернуть расширенный код ошибки	Целое число, Строка	код, указывающий на причину возникновения ошибки, текст ошибки
            2	Вернуть статус блокировок по ФН	Целое число	Возвращается битовая маска, значения бит указаны в соответствующей таблице
            */
            //RequestDataBuilder _builder = new RequestDataBuilder();
            //_builder.Write("1", false); // Вернуть расширенный код ошибки
            //this.sendCommand(CommandEnum.x06_ErrorInfo, _builder.ToArray(), true);
            return this.sendCommand(CommandEnum.x06_ErrorInfo, "1", true);
        }

        /// <summary>
        /// 0x10 Начало работы с ККТ
        /// </summary>
        public void StartWork()
        {
            /*
             Входные параметры
            (Дата)Текущая дата
            (Время)Текущее время
            */
            RequestDataBuilder _builder = new RequestDataBuilder();
            DateTime _dt = DateTime.Now;
            _builder
                .Write(_dt.ToString("ddMMyy")) // Дата
                .Write(_dt.ToString("HHmmss")); // Время
            this.sendCommand(CommandEnum.x10_StartWork, _builder.ToArray(), false);
        }

        /// <summary>
        /// 0x23 Открыть смену
        /// </summary>
        public void OpenShift()
        {
            RequestDataBuilder _builder = new RequestDataBuilder();
            _builder.Write(this.CashierName); // Имя оператора
            this.sendCommand(CommandEnum.x23_Open_Shift, _builder.ToArray(), false);
        }

        /// <summary>
        /// 0x21 Закрыть смену
        /// </summary>
        public void CloseShift()
        {
            RequestDataBuilder _builder = new RequestDataBuilder();
            _builder.Write(this.CashierName); // Имя оператора
            _builder.Write("0");
            this.sendCommand(CommandEnum.x21_Z_Report_Close_Shift, _builder.ToArray(), false);
        }

        /// <summary>
        /// 0x20 Сформировать отчет без гашения
        /// </summary>
        public void XReport()
        {
            RequestDataBuilder _builder = new RequestDataBuilder();
            _builder.Write(this.CashierName); // Имя оператора
            this.sendCommand(CommandEnum.x20_X_Report, _builder.ToArray(), false);
        }

        /// <summary>
        /// 0x30 Открыть документ
        /// </summary>
        public ICashReceiptResponse OpenDocument(DocumentTypeEnum docType)
        {
            /*
            Входные параметры
            (Целое число) Режим и тип документа
            (Целое число 1..99) Номер отдела
            (Имя оператора) Имя оператора
            (Целое число) Номер документа
            (Число 0..5) Система налогообложения (Тег 1055)
            */

            /*
            Режим и тип документа

            Номер бита	Пояснение
            0..3	Тип открываемого документа.
                1 - Сервисный документ,
                2 - Чек на продажу (приход),
                3 - Чек на возврат (возврат прихода),
                4 - Внесение в кассу,
                5 - Инкассация,
                6 - Чек на покупку (расход),
                7 - Чек на возврат покупки (возврат расхода)
            4	0 - Обычный режим формирования документа, 1 - Пакетный режим формирования документа
            5	0 - Обычный режим печати реквизитов, 1 - Режим отложенной печати реквизитов
            7	0 - Обычный режим печати чека, 1 - Чек не печатается; Реализована , начиная с версий 565.1.13 и 665.4.13
            */
            RequestDataBuilder _builder = new RequestDataBuilder();
            _builder
                .Write(((byte)docType).ToString()) // Чек на продажу (приход)
                .Write("1") // Номер отдела
                .Write(this.CashierName) // Имя оператора
                .Write(); // Номер документа. PS румерация выполняется самой кассой, так что наверное "0"

            /*
            Система налогообложения

            Значение	Пояснение
            0	Общая
            1	Упрощенная Доход
            2	Упрощенная Доход минус Расход
            3	Единый налог на вмененный доход
            4	Единый сельскохозяйственный налог
            5	Патентная система налогообложения
            */
            _builder.Write(); // Система налогообложения
            return this.sendCommand(CommandEnum.x30_Open_Document, _builder.ToArray(), true);
        }

        /// <summary>
        /// 0x42 Добавить товарную позицию
        /// </summary>
        public ICashReceiptResponse AddItem(string itemName, string article, decimal qty, decimal price, decimal discountSumm, PaymentTypeEnum paymentType, ItemTypeEnum itemType)
        {
            /*
            Входные параметры
            (Строка[0...256]) Название товара
            (Строка[0..18]) Артикул товара/номер ТРК
            (Дробное число) Количество товара в товарной позиции
            (Дробное число[0..99999999.99]) Цена товара по данному артикулу
            (Целое число) Номер ставки налога
            (Строка[0..4]) Номер товарной позиции
            (Целое число 1..16) Номер секции
            (Целое число) Тип скидки/наценки
            (Строка) Зарезервированно
            (Дробное число) Сумма скидки
            (Целое число) Признак способа расчета (Тег 1214)
            (Целое число) Признак предмета расчета (Тег 1212)
            (Строка[3]) Код страны происхождения товара (Тег 1230)
            (Строка[0...32]) Номер таможенной декларации (Тег 1231)
            (Дробное число) Сумма акциза (Тег 1229)
            */

            /*
            Признак способа расчета:

            Значение	Пояснение
            1	Предоплата 100%
            2	Предоплата
            3	Аванс
            4	Полный расчет
            5	Частичный расчет и кредит
            6	Передача в кредит
            7	Оплата кредита
            Если параметр не передан, по умолчанию выбирается признак способа расчёта 4 (полный расчёт).
            */

            /*
            Признак предмета расчета: --- их много больше, выбрал НАШИ

            Значение реквизита	Название товара содержит сведения
            1	о реализуемом товаре, за исключением подакцизного товара (наименование и иные сведения, описывающие товар)
            4	об оказываемой услуге (наименование и иные сведения, описывающие услугу)
            */
            RequestDataBuilder _builder = new RequestDataBuilder();
            _builder
                .Write(itemName) // Название товара
                .Write(article) // Артикул товара
                .Write(qty.ToString()) // Количество товара
                .Write(Math.Round(price, 9).ToString(DECIMAL_FORMAT, NumberFormatInfo.InvariantInfo)) // Цена товара
                .Write("3") // Ставка налога - 0/0
                .Write() // Номер товарной позиции
                .Write() // Номер секции
                .Write(discountSumm == 0 ? "0" : "2") // Тип скидки/наценки. 0 или пусто - нет скидки; 2 - скидка; 4 - наценка
                .Write() // Зарезервировано
                .Write(discountSumm.ToString(DECIMAL_FORMAT, NumberFormatInfo.InvariantInfo)) // Сумма скидки
                .Write(((int)paymentType).ToString()) // Признак способа расчета
                .Write(((int)itemType).ToString()) // Признак предмета расчета
                .Write("000") // Код страны происхождения товара
                .Write() // Номер таможенной декларации
                .Write("0"); // Сумма акциза

            return this.sendCommand(CommandEnum.x42_Add_Item, _builder.ToArray(), true);
        }

        /// <summary>
        /// 0x47 Оплата
        /// </summary>
        public void AddPayment(bool isCash, decimal summ)
        {
            /*
             Входные параметры
            (Целое число 0..15) Код типа платежа
            (Дробное число) Сумма, принятая от покупателя по данному платежу
            (Строка[0..44]) Дополнительный текст

            32 - Названия типов платежей (Массив 0..15)
            0 - (Строка[0..18]) Зарезервирован типом “НАЛИЧНЫМИ” (только чтение)
            1 - (Строка[0..18]) Зарезервирован типом “ЭЛЕКТРОННЫМИ” (только чтение)
            2..12 - (Строка[0..18]) Пользовательские строки наименования платежа
            13 - (Строка[0..18]) Зарезервирован типом "ПРЕДВАРИТЕЛЬНАЯ ОПЛАТА (АВАНС)" (только чтение)
            14 - (Строка[0..18]) Зарезервирован типом "ПОСЛЕДУЮЩАЯ ОПЛАТА (КРЕДИТ)" (только чтение)
            15 - (Строка[0..18]) Зарезервирован типом "ИНАЯ ФОРМА ОПЛАТЫ" (только чтение)
            */
            RequestDataBuilder _builder = new RequestDataBuilder();
            _builder
                .Write(isCash ? "0" : "1") // Код типа платежа
                .Write(summ.ToString(DECIMAL_FORMAT, NumberFormatInfo.InvariantInfo)) // Сумма
                .Write(); // Доп текст
            this.sendCommand(CommandEnum.x47_RegisterPayment, _builder.ToArray(), false);
        }

        /// <summary>
        /// 0x31 Завершить документ
        /// </summary>
        public ICashReceiptResponse CloseDocument()
        {
            /*
            Входные параметры
            (Целое число) Флаг отрезки
            (Строка)[0..256] Адрес покупателя (Тег 1008)
            (Число) Разные флаги
            (Строка) Зарезервировано
            (Строка) Зарезервировано
            (Строка) Зарезервировано
            (Строка) Наименование дополнительного реквизита пользователя (Тег 1085)
            (Строка) Значение дополнительного реквизита пользователя (Тег 1086)
            (Строка)[0..128] Покупатель (Тег 1227)
            (Строка)[0..12] ИНН покупателя (Тег 1228)
             */
            RequestDataBuilder _builder = new RequestDataBuilder();
            _builder
                .Write("5") // Отрезка чека не выполняется
                .Write() // Адрес покупателя (номер телефона или e-mail для электронного чека)
                .Write("0") // Разные флаги
                .Write() // Зарезервировано 1
                .Write() // Зарезервировано 2
                .Write() // Зарезервировано 3
                .Write() // Дополнительные реквизиты пользователя (Тег 1085)
                .Write() // Дополнительные реквизиты пользователя (Тег 1086)
                .Write() // Покупатель
                .Write(); // ИНН покупателя
            return this.sendCommand(CommandEnum.x31_Close_Document, _builder.ToArray(), true);
            //for(int _i = 0; _i < 3; _i++)
            //    this.SendLF();
        }

        /// <summary>
        /// 0x32 Аннулировать документ
        /// </summary>
        public void AnnulateDocument()
        {
            this.sendCommand(CommandEnum.x32_Annuate_Document, String.Empty, false);
        }

        public void CloseViki()
        {
            if (this.isContinueRead)
            {
                this.isContinueRead = false;
                if (this.comPort.IsOpen)
                {
                    this.readThread.Join();
                    this.comPort.Close();
                }
            }
        }

        ~VikiPrint()
        {
            this.isContinueRead = false;
            if (this.comPort.IsOpen)
            {
                this.readThread.Join();
                this.comPort.Close();
            }
        }

        //public void PrintString(string text)
        //{
        //    libPrintString(text, null);

        //    MData test = new MData();
        //    libCloseDocument(ref test, 1);
        //}

        //public void RegisterProduct(string name, string barcode, double quantity, double price, int numPos = 1)
        //{
        //    libAddPosition(ConvertTo866(name), ConvertTo866(barcode), quantity, price, taxNumber, numPos, numDepart, 0, "", 0);
        //}

        //public void AnnulateProduct(string name, double quantity, double price)
        //{
        //    int numPos = 1;
        //    libAddPosition(ConvertTo866(name), ConvertTo866(name), quantity, price, taxNumber, numPos, numDepart, 0, "", 0);
        //}

        //public void Storning(string name, double quantity, double price)
        //{
        //    int numPos = 1;
        //    libAddPosition(ConvertTo866(name), ConvertTo866(name), quantity, price, taxNumber, numPos, numDepart, 0, "", 0);
        //}

        //public void RegisterPayment(double sum, byte type = 0)
        //{
        //    int result = libAddPaymentD(type, sum, "");
        //}

        //public void PrintTotal()
        //{
        //    libSubTotal();
        //}

        //public void RegisterDiscount(byte type, string nameDiscount, int sum)
        //{
        //    libAddDiscount(type, nameDiscount, sum);
        //}

        //public void PrintServiceData()
        //{
        //    libPrintServiceData();
        //}

        //public void OpenSession()
        //{
        //    libOpenShift(cashierName);
        //}

        //public void CloseSession()
        //{
        //    libPrintZReport(cashierName, 0);
        //}

        //public bool IsSessionOpen()
        //{
        //    int fatalStatus, currentFlagsStatus, documentStatus;
        //    int flagsStatus = getStatusFlags(out fatalStatus, out currentFlagsStatus, out documentStatus);

        //    //MessageBox.Show(flagsStatus.ToString() + '-' + fatalStatus.ToString() + '-' + currentFlagsStatus.ToString() + '-' + documentStatus.ToString());

        //    if (flagsStatus == 0)
        //    {
        //        if (currentFlagsStatus == 12)
        //        {
        //            //MessageBox.Show("Смена длится более 24 часов. Перезапустите смену.");
        //            return true;
        //        }

        //        if (currentFlagsStatus == 4)
        //            return true;
        //        else
        //            return false;
        //    }

        //    return false;
        //}

        //private string ConvertTo866(string str)
        //{
        //    Encoding _cp866 = Encoding.GetEncoding("CP866");
        //    Encoding _win1251 = Encoding.GetEncoding("windows-1251");
        //    return _cp866.GetString(Encoding.Convert(_win1251, _cp866, _win1251.GetBytes(str)));//System.Text.Encoding.Default.GetString(Encoding.Convert(Encoding.Default, Encoding.GetEncoding("CP866"), Encoding.Default.GetBytes(str)));
        //}

        /* public void BotIndent()
         {

         }*/
        //    public void cashierSign()
        //    {

        //    }

        //    public void buyerSign()
        //    {

        //    }

        //    public void setTax_id(int tax_id)
        //    {

        //    }

        //    public int getStatus()// надо новое имя
        //    {
        //        return status;
        //    }

        //    public string AdvSetting(int com_port, int com_speed, bool use_remote, string ip, int model)
        //    {
        //        return "";
        //    }

        //    public string Get_info()
        //    {
        //        return "";
        //    }
    }
}
