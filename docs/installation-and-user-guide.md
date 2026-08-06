# DieCut Catalog: установка и работа

Актуально на 22 июля 2026 года.

DieCut Catalog — сетевая система FLEXPRINT для хранения, поиска и производственного учёта вырубных ножей. Windows-клиенты работают с единым API на Ubuntu-сервере. PostgreSQL хранит карточки, сотрудников и журнал событий, PDF-чертежи находятся в серверном файловом хранилище.

## Возможности

- каталог с вкладками по оборудованию, поиском и фильтрами;
- карточка ножа и статусы **OK**, **Требует проверки**, **Списан**, **Заказать новый**;
- учёт тиража в штуках, погонных метрах и оборотах;
- журнал тиражей, сбросов, списаний и PDF;
- импорт Excel и PDF;
- генерация векторного PDF в масштабе 1:1;
- сотрудники, фотографии, контакты, смена почты и пароля;
- административное подтверждение опасных операций.

## Архитектура

~~~text
Windows-клиент WPF
        |
      HTTPS
        |
Ubuntu + reverse proxy
        |
ASP.NET Core API
   |             |
PostgreSQL   PDF-хранилище
~~~

Клиент не подключается напрямую к PostgreSQL и папке PDF. Все изменения проходят через API, который проверяет права, валидирует данные и записывает события в журнал.

## Требования

**Сервер:** Ubuntu Server 22.04/24.04 LTS, 2 CPU, 2 ГБ RAM (лучше 4 ГБ), от 20 ГБ диска, домен, порты 22/80/443, Docker Engine, Docker Compose plugin и SMTP.

**Клиент:** Windows 10/11 x64, доступ к HTTPS API, экран от 1600×900 и PDF-просмотрщик. Illustrator нужен только для проверки или обработки схем.

> Рабочим пользователям не нужны Docker, PostgreSQL и .NET SDK.

## Установка Ubuntu-сервера

### 1. Подготовка

~~~bash
sudo apt update
sudo apt upgrade -y
sudo apt install -y ca-certificates curl git
~~~

Docker устанавливайте из официального репозитория: https://docs.docker.com/engine/install/ubuntu/

~~~bash
sudo systemctl enable --now docker
docker --version
docker compose version
~~~

### 2. Проект

~~~bash
sudo mkdir -p /opt/diecut-catalog
sudo chown "$USER":"$USER" /opt/diecut-catalog
git clone https://github.com/wargie/DieCutCatalog.git /opt/diecut-catalog
cd /opt/diecut-catalog
~~~

### 3. Секреты

~~~bash
cp .env.example .env
chmod 600 .env
nano .env
~~~

~~~dotenv
POSTGRES_PASSWORD=СЛУЧАЙНЫЙ_ДЛИННЫЙ_ПАРОЛЬ
ACCOUNT_SETUP_TOKEN=СЛУЧАЙНЫЙ_ТОКЕН
SMTP_HOST=smtp.example.com
SMTP_PORT=587
SMTP_USERNAME=mailer@example.com
SMTP_PASSWORD=ПАРОЛЬ_SMTP
SMTP_FROM_ADDRESS=mailer@example.com
SMTP_FROM_NAME="DieCut Catalog"
SMTP_ENABLE_SSL=true
JUSTCUT_BASE_URL=http://api1c.justcut.ru:8081/jctest/hs/jcexch/
JUSTCUT_UID_CONTRAGENT=ИДЕНТИФИКАТОР_КОНТРАГЕНТА
JUSTCUT_ENVIRONMENT=Test
JUSTCUT_TIMEOUT_SECONDS=60
~~~

Генерация секрета: **openssl rand -base64 36**.

> Файл .env нельзя отправлять в Git, прикладывать к письмам или хранить с пользовательской инструкцией.

### 4. Запуск

~~~bash
cd /opt/diecut-catalog
docker compose -f compose.production.yaml up -d --build
docker compose -f compose.production.yaml ps
curl http://127.0.0.1:5080/health
~~~

Корректный ответ: **"Healthy"**. API слушает только **127.0.0.1:5080**; не открывайте этот порт напрямую наружу.

### 5. HTTPS

Рекомендуется Caddy: https://caddyserver.com/docs/install

~~~caddyfile
catalog.example.com {
    reverse_proxy 127.0.0.1:5080
}
~~~

~~~bash
sudo caddy validate --config /etc/caddy/Caddyfile
sudo systemctl reload caddy
curl https://catalog.example.com/health
~~~

DNS домена должен указывать на сервер. Для временной проверки допустим SSH-туннель **ssh -N -L 5080:127.0.0.1:5080 user@server**, для постоянной работы используйте HTTPS.

Текущий адрес FLEXPRINT: **https://diecutcatalog.duckdns.org**. Caddy установлен как системная служба и автоматически получает и продлевает сертификат Let''s Encrypt.

### 6. Первый администратор

~~~bash
curl -X POST https://catalog.example.com/api/setup/administrator \
  -H "Content-Type: application/json" \
  -H "X-Setup-Token: ЗНАЧЕНИЕ_ACCOUNT_SETUP_TOKEN" \
  -d '{
    "email": "admin@example.com",
    "firstName": "Имя",
    "lastName": "Фамилия",
    "position": "Администратор",
    "phone": "+7..."
  }'
~~~

Временный пароль приходит по почте. Новый пароль: минимум 12 символов, заглавная и строчная буквы, цифра и специальный символ. После создания администратора замените setup token и выполните **docker compose -f compose.production.yaml up -d api**.
## Windows-клиент

На ПК сборки установите .NET 8 SDK:

~~~powershell
git clone https://github.com/wargie/DieCutCatalog.git
cd DieCutCatalog
dotnet restore
./scripts/Build-ClientRelease.ps1 -Version 1.4.0
~~~

Папку **artifacts\client** упакуйте в ZIP. На рабочих ПК распакуйте, например, в **C:\Program Files\FLEXPRINT\DieCut Catalog** и запускайте **DieCutCatalog.Desktop.exe**.

> При обновлении закройте клиент и замените всю папку. База и PDF находятся на сервере и не затрагиваются.

## Первый вход

![Экран входа](images/login.png)

1. Введите HTTPS-адрес API, например **https://catalog.example.com**.
2. Введите электронную почту и пароль.
3. Нажмите **Войти**.
4. При первом входе замените временный пароль.

> При ошибке соединения откройте адрес **/health** в браузере. Должен быть ответ **"Healthy"** без предупреждения о сертификате.

## Каталог

![Каталог ножей](images/catalog.png)

Подсказки:

1. Вкладки **Все ножи**, **Nilpeter/Lesko**, **MarkAndy**, **Big Lesko**, **Label Source** ограничивают список по оборудованию.
2. Поиск работает по номеру, оборудованию, материалу и комментарию.
3. Выпадающие списки фильтруют каталог.
4. **Импорт Excel** загружает существующий каталог.
5. **Импорт PDF** распознаёт схему и открывает карточку для проверки.
6. **Новый нож** создаёт пустую карточку.
7. Щелчок по строке открывает карточку справа.

Порядок колонок: **№, Статус, Вал, L, B, Ручьи, Эт. в ручье, A1, A2, Материал, Ширина материала, Фигура, Дата, Оборудование, Заказ JC, Обороты, Комментарий.**

Целые значения показываются без нулевой дробной части. A1 и A2 отображаются с точностью до трёх знаков.

## Карточка ножа

![Карточка ножа](images/knife-card.png)

Карточка содержит номер, заказ JC, оборудование, материал, фигуру, L, B, ширину материала, вал, ручьи, этикетки в ручье, расстояние между ручьями, радиус, A1, A2, статус, дату, комментарий, тираж, пробег, обороты, PDF и журнал.

Оборудование: **Nilpeter/Lesko, MarkAndy, Big Lesko, Label Source**.

Фигуры: **прямоугольник, круг, квадрат, специальная форма, перфорация**.

> Вал — только целое положительное число.

## Учёт тиража

Вводится только тираж этикеток в штуках.

~~~text
Пробег, м =
  тираж / количество ручьёв
  × (длина этикетки B, мм + horizontal break, мм)
  / 1000

Раппорт, м = вал × 3,175 / 1000

Обороты = округление вверх(пробег / раппорт)
~~~

Обороты всегда целые. Тираж фиксируется в журнале с датой, сотрудником и значениями до/после.

**Сбросить тираж**, **Списать нож** и **Удалить нож** требуют пароль администратора. Обычный пароль отклоняется как недостаточный по правам.

## PDF

**Создать PDF** формирует новую векторную версию в масштабе 1:1:

- контур соответствует L × B;
- рамка соответствует ширине материала;
- раппорт равен вал × 3,175 мм;
- учитываются ручьи, повторения, расстояние и радиус;
- выводятся Corner radius, vertical break и horizontal break.

Каждая генерация создаёт отдельный PDF и событие журнала. Старые версии не перезаписываются. **Загрузить PDF** прикрепляет схему к ножу. **Импорт PDF** создаёт новую карточку, но распознанные значения обязательно проверяются пользователем.

> В Illustrator проверяйте размер самого векторного контура, а не страницы или группы объектов.

## Сотрудники

![Раздел сотрудника](images/employee.png)

Администратор создаёт сотрудника по электронной почте. Сервер отправляет временный пароль через SMTP. В профиле можно изменить имя, должность, телефон, контакты, фото, электронную почту и пароль. Поддерживаются JPEG, PNG, WebP до 5 МБ.
## Резервное копирование

Резервировать нужно PostgreSQL и PDF-хранилище.

### PostgreSQL

~~~bash
mkdir -p /opt/backups/diecut
docker compose -f /opt/diecut-catalog/compose.production.yaml exec -T postgres \
  pg_dump -U diecut_catalog -d diecut_catalog -Fc \
  > /opt/backups/diecut/database-$(date +%F).dump
~~~

### PDF

~~~bash
docker run --rm \
  -v diecut-catalog_document-storage:/data:ro \
  -v /opt/backups/diecut:/backup \
  alpine \
  tar -czf /backup/documents-$(date +%F).tar.gz -C /data .
~~~

Имя volume проверьте через **docker volume ls**. Храните вторую копию вне сервера и регулярно проверяйте восстановление.

## Обновление

~~~bash
cd /opt/diecut-catalog
git pull --ff-only
docker compose -f compose.production.yaml up -d --build
curl http://127.0.0.1:5080/health
~~~

Перед крупным обновлением сделайте резервную копию базы и PDF.

## Диагностика

~~~bash
docker compose -f compose.production.yaml ps
docker compose -f compose.production.yaml logs --tail=200 api
docker compose -f compose.production.yaml logs --tail=200 postgres
curl http://127.0.0.1:5080/health
~~~

Если не приходит временный пароль, проверьте SMTP и выполните:

~~~bash
docker compose -f compose.production.yaml up -d --force-recreate api
~~~

Коды:

- **401** — войдите повторно;
- **403** — недостаточно прав;
- **429** — слишком много попыток входа, подождите минуту;
- **500** — проверьте журнал API за время ошибки.

Не отправляйте пароли и содержимое .env в отчёте об ошибке.

## Контрольный список

- [ ] Сервер защищён SSH-ключами.
- [ ] Docker и Compose установлены.
- [ ] .env заполнен и имеет права 600.
- [ ] Контейнеры работают.
- [ ] /health возвращает "Healthy".
- [ ] HTTPS-сертификат действителен.
- [ ] Создан первый администратор.
- [ ] SMTP доставляет временные пароли.
- [ ] Клиент запускается минимум на двух ПК.
- [ ] Проверены импорт Excel и PDF.
- [ ] Создан и открыт тестовый PDF.
- [ ] Проверены тираж и журнал.
- [ ] Проверена резервная копия.

## Ссылки

- Репозиторий: https://github.com/wargie/DieCutCatalog
- Docker Engine: https://docs.docker.com/engine/install/ubuntu/
- Docker Compose: https://docs.docker.com/compose/install/linux/
- Caddy: https://caddyserver.com/docs/install
- Архитектура: [docs/architecture.md](architecture.md)
