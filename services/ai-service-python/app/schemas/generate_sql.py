from pydantic import BaseModel, Field


class ColumnMetadata(BaseModel):
    name: str
    type: str
    nullable: bool | None = None
    semanticHint: str | None = None


class TableMetadata(BaseModel):
    tableName: str
    description: str | None = None
    columns: list[ColumnMetadata]


class RelationshipMetadata(BaseModel):
    fromTable: str
    fromColumn: str
    toTable: str
    toColumn: str
    relationType: str | None = None


class SchemaMetadata(BaseModel):
    dialect: str = "postgresql"
    tables: list[TableMetadata]
    relationships: list[RelationshipMetadata] = Field(default_factory=list)


class GenerateSqlRequest(BaseModel):
    naturalLanguageQuery: str = Field(min_length=3)
    schemaMetadata: SchemaMetadata


class SqlProposal(BaseModel):
    dialect: str = "postgresql"
    sql: str
    parameters: list[dict] = Field(default_factory=list)


class ExplanationMetadata(BaseModel):
    intentSummary: str
    reasoningSummary: str
    selectedTables: list[str] = Field(default_factory=list)
    selectedColumns: list[str] = Field(default_factory=list)
    assumptions: list[str] = Field(default_factory=list)
    confidenceScore: float = Field(ge=0, le=1)
    warnings: list[str] = Field(default_factory=list)


class GenerateSqlResponse(BaseModel):
    sqlProposal: SqlProposal
    explanationMetadata: ExplanationMetadata
