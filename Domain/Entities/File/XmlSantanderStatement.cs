using System.Xml.Serialization;
using Domain.Common;
using Domain.Errors;
using Microsoft.AspNetCore.Http;
// ReSharper disable All

namespace Domain.Entities.File;

public class XmlSantanderStatement
{
    public Statement Statement { get; private set; }

    public Result XmlSantanderStatementCreate(IFormFile file)
    {
        using (var stream = file.OpenReadStream())
        {
            var xmlSantanderStatementDeserialization = DeserializeFile(stream);
            return xmlSantanderStatementDeserialization;
        }
    }

    private Result DeserializeFile(Stream xmlStream)
    {
        XmlSerializer serializer = new XmlSerializer(typeof(Statement));
        try
        {
            Statement = (Statement) serializer.Deserialize(xmlStream);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(DomainErrors.StatementFile.ErrorWhileDeserializationStatementFile(ex));
        }
    }
}
[XmlRoot("statement", Namespace = "")]
public class Statement
{
    [XmlElement("bank-unit")]
    public BankUnit BankUnit { get; set; }

    [XmlElement("customer")]
    public Customer Customer { get; set; }

    [XmlElement("account")]
    public Account Account { get; set; }

    [XmlElement("stmt")]
    public StatementInfo StatementInfo { get; set; }

    [XmlElement("transactions")]
    public Transactions Transactions { get; set; }
}

public class BankUnit
{
    [XmlElement("bank-name")]
    public string BankName { get; set; }

    [XmlElement("name")]
    public string Name { get; set; }

    [XmlElement("address")]
    public string Address { get; set; }

    [XmlElement("post-code")]
    public string PostCode { get; set; }

    [XmlElement("locality")]
    public string Locality { get; set; }

    [XmlElement("phone")]
    public string Phone { get; set; }

    [XmlElement("nbp-no")]
    public string NbpNo { get; set; }
}

public class Customer
{
    [XmlElement("cif")]
    public string Cif { get; set; }

    [XmlElement("name")]
    public string Name { get; set; }

    [XmlElement("address-line1")]
    public string AddressLine1 { get; set; }

    [XmlElement("address-line2")]
    public string AddressLine2 { get; set; }

    [XmlElement("address-line3")]
    public string AddressLine3 { get; set; }

    [XmlElement("country")]
    public string Country { get; set; }
}

public class Account
{
    [XmlElement("account-no")]
    public string AccountNo { get; set; }

    [XmlElement("iban")]
    public string Iban { get; set; }

    [XmlElement("product-desc")]
    public string ProductDesc { get; set; }

    [XmlElement("currency")]
    public string Currency { get; set; }

    [XmlElement("value")]
    public string Value { get; set; }

    [XmlElement("limit-value")]
    public string LimitValue { get; set; }

    [XmlElement("interest-rate")]
    public string InterestRate { get; set; }

    [XmlElement("interest-date")]
    public string InterestDate { get; set; }
}

public class StatementInfo
{
    [XmlElement("stmt-no")]
    public string StatementNo { get; set; }

    [XmlElement("begin")]
    public string Begin { get; set; }

    [XmlElement("begin-value")]
    public string BeginValue { get; set; }

    [XmlElement("end")]
    public string End { get; set; }

    [XmlElement("end-value")]
    public string EndValue { get; set; }
}

public class Transactions
{
    [XmlElement("trn")]
    public List<Transaction> TransactionList { get; set; }
}

public class Transaction
{
    [XmlElement("trn-code")]
    public string TransactionCode { get; set; }

    [XmlElement("exe-date")]
    public string ExecutionDate { get; set; }

    [XmlElement("creat-date")]
    public string CreationDate { get; set; }

    [XmlElement("value")]
    public string Value { get; set; }

    [XmlElement("acc-value")]
    public string AccountValue { get; set; }

    [XmlElement("real-value")]
    public string RealValue { get; set; }

    [XmlElement("desc-base")]
    public string DescriptionBase { get; set; }

    [XmlElement("desc-opt")]
    public string DescriptionOpt { get; set; }
}

