namespace NGB.Definitions.Documents.Actions;

public interface IDocumentActionDefinitionsContributor
{
    void Contribute(DocumentActionDefinitionsBuilder builder);
}
