# workers/ dizininden derlenir:  docker build -t govai-worker workers/
FROM python:3.12-slim AS base

# Debian ve PyPI'ın varsayılan adresleri bazı ağlardan erişilemez olabilir (Fastly kesintisi,
# ülke bazlı engel). Boş bırakılırsa varsayılan kaynaklar kullanılır; doldurulursa ayna devreye girer.
ARG APT_MIRROR=""
ARG PIP_INDEX_URL=""

ENV PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PIP_NO_CACHE_DIR=1

WORKDIR /app

# lxml ve pypdf için gerekli derleme/çalışma bağımlılıkları.
RUN if [ -n "$APT_MIRROR" ] && [ -f /etc/apt/sources.list.d/debian.sources ]; then \
        sed -i "s|http://deb.debian.org|$APT_MIRROR|g" /etc/apt/sources.list.d/debian.sources; \
    fi \
    && apt-get update \
    && apt-get install -y --no-install-recommends libxml2 libxslt1.1 \
    && rm -rf /var/lib/apt/lists/*

COPY pyproject.toml ./
COPY govai_workers/ ./govai_workers/

RUN if [ -n "$PIP_INDEX_URL" ]; then pip config set global.index-url "$PIP_INDEX_URL"; fi \
    && pip install --upgrade pip \
    && pip install .

RUN useradd --uid 10001 --create-home --shell /usr/sbin/nologin govai
USER govai

# Varsayılan olarak collector çalışır; compose içinde her servis kendi komutunu verir.
CMD ["govai-collector"]
