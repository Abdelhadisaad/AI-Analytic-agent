from fastapi import APIRouter

from app.core.generate_sql_service import GenerateSqlService
from app.core.intent_service import IntentService
from app.core.llm_client import LlmClient
from app.schemas.generate_sql import GenerateSqlRequest, GenerateSqlResponse

router = APIRouter()


@router.post("/generate-sql", response_model=GenerateSqlResponse)
async def generate_sql(request: GenerateSqlRequest) -> GenerateSqlResponse:
    llm_client = LlmClient()
    intent_service = IntentService(llm_client)
    service = GenerateSqlService(llm_client, intent_service)
    return await service.execute(request)
