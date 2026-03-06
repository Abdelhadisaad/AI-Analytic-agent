from fastapi import APIRouter

from app.core.generate_sql_service import GenerateSqlService
from app.core.llm_client import LlmClient
from app.schemas.generate_sql import GenerateSqlRequest, GenerateSqlResponse

router = APIRouter()


@router.post("/generate-sql", response_model=GenerateSqlResponse)
async def generate_sql(request: GenerateSqlRequest) -> GenerateSqlResponse:
    service = GenerateSqlService(LlmClient())
    return await service.execute(request)
