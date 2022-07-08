using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Dreamkas
{
    public enum CommandEnum : byte
    {
        x00_Status_Flags = 0x00,
        x06_ErrorInfo = 0x06,
        x10_StartWork = 0x10,
        x20_X_Report = 0x20,
        x21_Z_Report_Close_Shift = 0x21,
        x23_Open_Shift = 0x23,
        x30_Open_Document = 0x30,
        x31_Close_Document = 0x31,
        x32_Annuate_Document = 0x32,
        x40_Print_Text = 0x40,
        x42_Add_Item = 0x42,
        x44_Middle_Summ = 0x44,
        x45_SetDiscount = 0x45,
        x47_RegisterPayment = 0x47,
        x73_Print_Archived_Document = 0x73,
        x94_Print_Service_Data = 0x94
    }

    public interface ICashReceiptResponse
    {
        //bool LoadFromMessage(byte[] data);
        CommandEnum ResponseType { get; }
        string Data { get; }
    }
    public class ErrorResponse : ICashReceiptResponse
    {
        public ErrorResponse(CommandEnum commandType, string data, byte errorCode)
        {
            this.ErrorCode = errorCode;
            this.ResponseType = commandType;
            this.Data = data;
        }

        public byte ErrorCode { get; private set; }
        public CommandEnum ResponseType { get; private set; }
        public string Data { get; private set; }
    }
    public class x06_ErrorInfoResponse : ICashReceiptResponse
    {
        public x06_ErrorInfoResponse(string data)
        {
            /*
             Ответные параметры
            (Целое число) Номер запроса
            Возвращаемые данные. Тип и количество возвращаемых данных зависит от номера запроса

            Номер запроса (DEC)	Наименование Запроса	Формат возвращаемых данных	Комментарии
            1	Вернуть расширенный код ошибки	Целое число, Строка	код, указывающий на причину возникновения ошибки, текст ошибки
            2	Вернуть статус блокировок по ФН	Целое число	Возвращается битовая маска, значения бит указаны в соответствующей таблице
            */
            this.Data = data;
            string[] _dataSplitted = data.Split((char)VikiPrint.FS);
            try
            {
                this.RequestNumber = int.Parse(_dataSplitted[0]);
                switch (this.RequestNumber)
                {
                    case 1:
                        this.ExErrorNumber = int.Parse(_dataSplitted[1]);
                        this.ExErrorText = _dataSplitted[2];
                        break;
                    case 2:
                        break;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Поле данных для x06_ErrorInfo_Response имеет неверный формат!", ex);
            }
        }

        public int RequestNumber { get; private set; }
        public int ExErrorNumber { get; private set; }
        public string ExErrorText { get; private set; }

        public string Data { get; private set; }
        public CommandEnum ResponseType { get { return CommandEnum.x06_ErrorInfo; } }
    }
    public class x00_StatusFlagsResponse : ICashReceiptResponse
    {
        private List<string> fatalStatuses;
        private List<string> flags;
        private List<string> docStatuses;
        public x00_StatusFlagsResponse(string data)
        {
            this.Data = data;
            //int _length = data.Length;
            int _fatal, _docStatus, _flags;
            string[] _dataSplitted = data.Split((char)VikiPrint.FS);//(new char[] { (char)VikiPrint.FS }, StringSplitOptions.RemoveEmptyEntries);
            //if (_dataSplitted.Length != 4) // 4 вместо 3, т.к. последнее поле будет пустым
            //    throw new InvalidDataException("Поле данных для x00_StatusFlags_Response имеет неверный формат!");
            try
            {
                _fatal = int.Parse(_dataSplitted[0]);
                _flags = int.Parse(_dataSplitted[1]);
                _docStatus = int.Parse(_dataSplitted[2]);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Поле данных для x00_StatusFlags_Response имеет неверный формат!", ex);
            }

            if (_fatal == 0)
                this.fatalStatuses = null;
            else
            {
                this.fatalStatuses = new List<string>();
                if ((_fatal & 0b_0000_0000_0000_0001) > 0)
                    this.fatalStatuses.Add("Неверная контрольная сумма NVR");
                if ((_fatal & 0b_0000_0000_0000_0010) > 0)
                    this.fatalStatuses.Add("Неверная контрольная сумма в конфигурации");
                if ((_fatal & 0b_0000_0000_0000_0100) > 0)
                    this.fatalStatuses.Add("Нет связи с ФН");
                if ((_fatal & 0b_0000_0000_0000_1000) > 0)
                    this.fatalStatuses.Add("Зарезервировано");
                if ((_fatal & 0b_0000_0000_0001_0000) > 0)
                    this.fatalStatuses.Add("Зарезервировано");
                if ((_fatal & 0b_0000_0000_0010_0000) > 0)
                    this.fatalStatuses.Add("ККТ не авторизовано");
                if ((_fatal & 0b_0000_0000_0100_0000) > 0)
                    this.fatalStatuses.Add("Фатальная ошибка ФН");
                if ((_fatal & 0b_0000_0000_1000_0000) > 0)
                    this.fatalStatuses.Add("Зарезервировано");
                if ((_fatal & 0b_0000_0001_00000_000) > 0)
                    this.fatalStatuses.Add("SD карта отсутствует или неисправна");
            }

            if (_flags == 0)
                this.flags = null;
            else
            {
                this.flags = new List<string>();
                if ((_flags & 0b_0000_0000_0000_0001) > 0)
                    this.flags.Add("Не выполнена команда 'Начало работы'");
                if ((_flags & 0b_0000_0000_0000_0010) > 0)
                    this.flags.Add("Нефискальный режим");
                if ((_flags & 0b_0000_0000_0000_0100) > 0)
                    this.flags.Add("Смена открыта");
                if ((_flags & 0b_0000_0000_0000_1000) > 0)
                    this.flags.Add("Смена больше 24 часов");
                if ((_flags & 0b_0000_0000_0001_0000) > 0)
                    this.flags.Add("Архив ФН закрыт");
                if ((_flags & 0b_0000_0000_0010_0000) > 0)
                    this.flags.Add("ФН не зарегистрирован");
                if ((_flags & 0b_0000_0000_0100_0000) > 0)
                    this.flags.Add("Зарезервировано");
                if ((_flags & 0b_0000_0000_1000_0000) > 0)
                    this.flags.Add("Зарезервировано");
                if ((_flags & 0b_0000_0001_0000_0000) > 0)
                    this.flags.Add("Не было завершено закрытие смены, необходимо повторить операцию");
                if ((_flags & 0b_0000_0010_0000_0000) > 0)
                    this.flags.Add("Ошибка контрольной ленты");
            }

            this.docStatuses = new List<string>();
            int _docType = _docStatus & 0x00001111;
            switch (_docType)
            {
                case 0:
                    this.docStatuses.Add("документ закрыт");
                    break;
                case 1:
                    this.docStatuses.Add("сервисный документ");
                    break;
                case 2:
                    this.docStatuses.Add("чек на продажу(приход)");
                    break;
                case 3:
                    this.docStatuses.Add("чек на возврат(возврат прихода)");
                    break;
                case 4:
                    this.docStatuses.Add("внесение в кассу");
                    break;
                case 5:
                    this.docStatuses.Add("инкассация");
                    break;
                case 6:
                    this.docStatuses.Add("чек на покупку(расход)");
                    break;
                case 7:
                    this.docStatuses.Add("чек на возврат покупки(возврат расхода)");
                    break;
            }
            _docStatus = _docStatus >> 4;
            switch (_docStatus)
            {
                case 0:
                    this.docStatuses.Add("документ закрыт");
                    break;
                case 1:
                    this.docStatuses.Add("устанавливается после команды 'открыть документ' (Для типов документа 2 и 3 можно добавлять товарные позиции)");
                    break;
                case 2:
                    this.docStatuses.Add("Устанавливается после первой команды 'Подытог'");
                    break;
                case 3:
                    this.docStatuses.Add("Устанавливается после второй команды 'Подытог' или после начала команды 'Оплата' (Можно только производить оплату различными типами платежных средств)");
                    break;
                case 4:
                    this.docStatuses.Add("Расчет завершен, требуется закрыть документ");
                    break;
                case 8:
                    this.docStatuses.Add("Документ закрыт в ФН, но чек не допечатан. Аннулирование документа невозможно, необходимо еще раз выполнить команду закрытия документа");
                    break;
            }
        }

        public IEnumerable<string> FatalErrors { get { return this.fatalStatuses == null ? null : this.fatalStatuses.AsReadOnly(); } }
        public IEnumerable<string> Flags { get { return this.flags == null ? null : this.flags.AsReadOnly(); } }
        public IEnumerable<string> DocStatuses { get { return this.docStatuses == null ? null : this.docStatuses.AsReadOnly(); } }
        public CommandEnum ResponseType { get { return CommandEnum.x00_Status_Flags; } }
        public string Data { get; private set; }
    }
    public class x42_AddItemResponse : ICashReceiptResponse
    {
        public x42_AddItemResponse(string data)
        {
            this.Data = data;
            this.TaxSumm = string.IsNullOrEmpty(data) ? (decimal)0 : decimal.Parse(data.Split((char)VikiPrint.FS)[0], NumberFormatInfo.InvariantInfo);
        }
        public decimal TaxSumm { get; private set; }
        public string Data { get; private set; }
        public CommandEnum ResponseType { get { return CommandEnum.x42_Add_Item; } }
    }
 
    /// <summary>
    /// Тип открываемого документа
    /// </summary>
    public enum DocumentTypeEnum : byte
    {
        /*
        1 - Сервисный документ,
        2 - Чек на продажу (приход),
        3 - Чек на возврат (возврат прихода),
        4 - Внесение в кассу,
        5 - Инкассация,
        6 - Чек на покупку (расход),
        7 - Чек на возврат покупки (возврат расхода)
         */
        /// <summary>
        /// Сервисный документ
        /// </summary>
        Service = 1,
        /// <summary>
        /// Чек на продажу (приход)
        /// </summary>
        Income = 2,
        /// <summary>
        /// Чек на возврат (возврат прихода)
        /// </summary>
        ReturnOfIncome = 3,
        /// <summary>
        /// Внесение в кассу
        /// </summary>
        MoneyDeposit = 4,
        /// <summary>
        /// Инкассация
        /// </summary>
        Collection = 5,
        /// <summary>
        /// Чек на покупку (расход)
        /// </summary>
        Expenses = 6,
        /// <summary>
        /// Чек на возврат покупки (возврат расхода)
        /// </summary>
        ReturnOfExpenses = 7

    }
    /// <summary>
    /// Признак способа расчета
    /// </summary>
    public enum PaymentTypeEnum : int
    {
        /*
        Значение    Пояснение
            1	Предоплата 100%
            2	Предоплата
            3	Аванс
            4	Полный расчет
            5	Частичный расчет и кредит
            6	Передача в кредит
            7	Оплата кредита
        */
        /// <summary>
        /// Предоплата 100%
        /// </summary>
        FullPrePayment = 1,
        /// <summary>
        /// Предоплата
        /// </summary>
        PartialPrePayment = 2,
        /// <summary>
        /// Аванс
        /// </summary>
        Avans = 3,
        /// <summary>
        /// Полный расчет
        /// </summary>
        FullPayment = 4,
        /// <summary>
        /// Частичный расчет и кредит
        /// </summary>
        PartialPaymentAndCredit = 5,
        /// <summary>
        /// Передача в кредит
        /// </summary>
        Credit = 6,
        /// <summary>
        /// Оплата кредита
        /// </summary>
        PaymentForCredit = 7
    }
    /// <summary>
    /// Признак предмета расчета
    /// </summary>
    public enum ItemTypeEnum : int
    {
        /// <summary>
        /// о реализуемом товаре, за исключением подакцизного товара (наименование и иные сведения, описывающие товар)
        /// </summary>
        Product = 1,
        /// <summary>
        /// об оказываемой услуге (наименование и иные сведения, описывающие услугу)
        /// </summary>
        Service = 4
    }
    public class UnknownResponse : ICashReceiptResponse
    {
        public UnknownResponse(CommandEnum responseType, string data)
        {
            this.ResponseType = responseType;
            this.Data = data;
        }

        public CommandEnum ResponseType { get; private set; }
        public string Data { get; private set; }
    }
    public class x31_CloseDocumentResponse : ICashReceiptResponse
    {
        public x31_CloseDocumentResponse(string data)
        {
            this.Data = data;
            /*
            Ответные параметры
            (Целое число) Сквозной номер документа
            (Строка) Операционный счетчик
            (Строка) Строка ФД и ФП
            (Число) ФД - номер фискального документа
            (Число) ФП - фискальный признак
            (Число) Номер смены
            (Число) Номер документа в смене
            (Строка) Дата документа
            (Строка) Время документа
            */

            string[] _dataSplitted = data.Split((char)VikiPrint.FS);//(new char[] { (char)VikiPrint.FS }, StringSplitOptions.RemoveEmptyEntries);
            //if (_dataSplitted.Length != 9) // 9 вместо 8, т.к. последнее поле будет пустым
            //    throw new InvalidDataException("Поле данных для x31_Close_Document имеет неверный формат!");
            try
            {
                this.DocumentNumber = int.Parse(_dataSplitted[0]);
                this.OperationalCounter = _dataSplitted[1];
                this.FdPf = _dataSplitted[2];
                this.FiscalNumber = int.Parse(_dataSplitted[3]);
                this.FiscalSign = long.Parse(_dataSplitted[4]);
                this.ShiftNumber = int.Parse(_dataSplitted[5]);
                this.ShifDocumentNumber = int.Parse(_dataSplitted[6]);
                if (_dataSplitted[7].Length == 6)
                    this.DateTime = new DateTime(
                        int.Parse(_dataSplitted[7].Substring(4, 2)) + 2000,
                        int.Parse(_dataSplitted[7].Substring(2, 2)),
                        int.Parse(_dataSplitted[7].Substring(0, 2)),
                        int.Parse(_dataSplitted[8].Substring(0, 2)),
                        int.Parse(_dataSplitted[8].Substring(2, 2)),
                        int.Parse(_dataSplitted[8].Substring(4, 2)));
                else if (_dataSplitted[7].Length == 8)
                    this.DateTime = new DateTime(
                        int.Parse(_dataSplitted[7].Substring(4, 4)),
                        int.Parse(_dataSplitted[7].Substring(2, 2)),
                        int.Parse(_dataSplitted[7].Substring(0, 2)),
                        int.Parse(_dataSplitted[8].Substring(0, 2)),
                        int.Parse(_dataSplitted[8].Substring(2, 2)),
                        int.Parse(_dataSplitted[8].Substring(4, 2)));
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Поле данных для x31_Close_Document имеет неверный формат!", ex);
            }
        }

        public int DocumentNumber { get; private set; }
        public string OperationalCounter { get; private set; }
        public string FdPf { get; private set; }
        public int FiscalNumber { get; private set; }
        public long FiscalSign { get; private set; }
        public int ShiftNumber { get; private set; }
        public int ShifDocumentNumber { get; private set; }
        public DateTime DateTime { get; private set; }

        public string Data { get; private set; }
        public CommandEnum ResponseType { get { return CommandEnum.x31_Close_Document; } }
    }

}
