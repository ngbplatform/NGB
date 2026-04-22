namespace NGB.Persistence.Migrations;

public interface IDdlObject
{
    string Name { get; }
    string Generate();
}
