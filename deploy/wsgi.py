"""Gunicorn WSGI entry point. Run from repository root."""

from python.api.flask_app import app

application = app
