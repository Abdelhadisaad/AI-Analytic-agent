from typing import Optional, Union

from pydantic import BaseModel, Field


class ColumnMetadata(BaseModel):
    columnName: str
    dataType: str
    isNullable: Optional[bool] = None
    description: Optional[str] = None


class TableMetadata(BaseModel):
    tableName: str
    description: Optional[str] = None
    columns: list[ColumnMetadata]


class RelationshipMetadata(BaseModel):
    fromTable: str
    fromColumn: str
    toTable: str
    toColumn: str
    relationType: Optional[str] = None


class SchemaMetadata(BaseModel):
    dialect: str = "postgresql"
    tables: list[TableMetadata]
    relationships: list[RelationshipMetadata] = Field(default_factory=list)


class GenerateSqlRequest(BaseModel):
    requestId: Optional[str] = None
    correlationId: Optional[str] = None
    naturalLanguageQuery: str = Field(min_length=3)
    locale: Optional[str] = None
    schemaMetadata: SchemaMetadata



class SqlParameter(BaseModel):
    name: str
    value: Optional[Union[str, int, float, bool]] = None


class SqlProposal(BaseModel):
    dialect: str = "postgresql"
    sql: str
    parameters: list[SqlParameter] = Field(default_factory=list)


class ExplanationMetadata(BaseModel):
    intentSummary: str
    reasoningSummary: str
    selectedTables: list[str] = Field(default_factory=list)
    selectedColumns: list[str] = Field(default_factory=list)
    assumptions: list[str] = Field(default_factory=list)
    confidenceScore: float = Field(ge=0, le=1)
    warnings: list[str] = Field(default_factory=list)


class GenerateSqlResponse(BaseModel):
    requestId: Optional[str] = None
    correlationId: Optional[str] = None
    sqlProposal: SqlProposal
    explanationMetadata: ExplanationMetadata
