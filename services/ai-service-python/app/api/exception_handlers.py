from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

from app.core.exceptions import LlmProviderError, LlmTimeoutError


def register_exception_handlers(app: FastAPI) -> None:
    @app.exception_handler(LlmTimeoutError)
    async def llm_timeout_handler(_: Request, ex: LlmTimeoutError) -> JSONResponse:
        return JSONResponse(
            status_code=504,
            content={
                "error": "llm_timeout",
                "message": str(ex),
            },
        )

    @app.exception_handler(LlmProviderError)
    async def llm_provider_handler(_: Request, ex: LlmProviderError) -> JSONResponse:
        return JSONResponse(
            status_code=502,
            content={
                "error": "llm_provider_error",
                "message": str(ex),
            },
        )

    @app.exception_handler(Exception)
    async def generic_handler(_: Request, __: Exception) -> JSONResponse:
        return JSONResponse(
            status_code=500,
            content={
                "error": "internal_server_error",
                "message": "Unexpected server error.",
            },
        )
