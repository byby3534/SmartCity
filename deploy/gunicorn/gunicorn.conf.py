import multiprocessing
import os

_repo_root = os.path.abspath(os.path.join(os.path.dirname(__file__), "../.."))

bind = os.getenv("GUNICORN_BIND", "127.0.0.1:5001")
workers = int(os.getenv("GUNICORN_WORKERS", min(4, multiprocessing.cpu_count())))
threads = int(os.getenv("GUNICORN_THREADS", "2"))
timeout = int(os.getenv("GUNICORN_TIMEOUT", "120"))
keepalive = 5
chdir = _repo_root
wsgi_app = "deploy.wsgi:application"
accesslog = "-"
errorlog = "-"
loglevel = os.getenv("GUNICORN_LOG_LEVEL", "info")
preload_app = True
