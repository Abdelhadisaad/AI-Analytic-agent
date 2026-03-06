from fastapi import FastAPI

from app.api.routes import router
from app.api.exception_handlers import register_exception_handlers

app = FastAPI(title="AI analytic agent - AI service", version="1.0.0")
register_exception_handlers(app)
app.include_router(router)
