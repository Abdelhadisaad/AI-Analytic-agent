from app.core.llm_client import LlmClient
from app.core.prompting import build_prompt
from app.schemas.generate_sql import GenerateSqlRequest, GenerateSqlResponse


class GenerateSqlService:
    def __init__(self, llm_client: LlmClient) -> None:
        self._llm_client = llm_client

    async def execute(self, request: GenerateSqlRequest) -> GenerateSqlResponse:
        prompt = build_prompt(request)
        llm_json = await self._llm_client.generate(prompt)
        return GenerateSqlResponse.model_validate(llm_json)
