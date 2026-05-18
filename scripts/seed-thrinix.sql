-- =============================================================
-- Thrinix — Onderhoud Contract Management Database
-- =============================================================
-- Thrinix ontwikkelt websites voor zakelijke klanten en beheert
-- jaarlijkse onderhoud contracten. Elke klant betaalt een vaste
-- maandelijkse vergoeding in ruil voor een aantal onderhoud uren.
-- Bij overschrijding: uurtje-factuurtje (€50/uur).
--
-- Tariefstructuur:
--   Basis     : €50/maand  → 12 uur/jaar
--   Standaard : €75/maand  → 18 uur/jaar
--   Uitgebreid: €100/maand → 24 uur/jaar
--   Premium   : €150/maand → 36 uur/jaar
-- =============================================================

CREATE TABLE IF NOT EXISTS clients (
    id            SERIAL PRIMARY KEY,
    company_name  VARCHAR(150) NOT NULL,
    type          VARCHAR(50)  NOT NULL,
    contact_email VARCHAR(255) UNIQUE NOT NULL,
    city          VARCHAR(100) NOT NULL,
    created_at    TIMESTAMP    NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS contracts (
    id             SERIAL PRIMARY KEY,
    client_id      INTEGER       REFERENCES clients(id),
    start_date     DATE          NOT NULL,
    end_date       DATE          NOT NULL,
    monthly_fee    NUMERIC(10,2) NOT NULL DEFAULT 75.00,
    hourly_rate    NUMERIC(10,2) NOT NULL DEFAULT 50.00,
    hours_included INTEGER       NOT NULL,
    status         VARCHAR(20)   NOT NULL DEFAULT 'active',
    created_at     TIMESTAMP     NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS maintenance_tasks (
    id             SERIAL PRIMARY KEY,
    contract_id    INTEGER       REFERENCES contracts(id),
    description    VARCHAR(300)  NOT NULL,
    category       VARCHAR(50)   NOT NULL,
    hours_spent    NUMERIC(4,2)  NOT NULL,
    performed_date DATE          NOT NULL,
    status         VARCHAR(20)   NOT NULL DEFAULT 'completed'
);

CREATE TABLE IF NOT EXISTS invoices (
    id             SERIAL PRIMARY KEY,
    contract_id    INTEGER       REFERENCES contracts(id),
    invoice_date   DATE          NOT NULL,
    extra_hours    NUMERIC(4,2)  NOT NULL,
    amount         NUMERIC(10,2) NOT NULL,
    status         VARCHAR(20)   NOT NULL DEFAULT 'pending'
);

TRUNCATE TABLE invoices, maintenance_tasks, contracts, clients RESTART IDENTITY CASCADE;

-- =============================================================
-- KLANTEN
-- =============================================================
INSERT INTO clients (company_name, type, contact_email, city) VALUES
('Lela Restaurant',            'restaurant',       'info@lelarestaurant.nl',       'Amsterdam'),
('AlSaif Barber',              'kapper',           'contact@alsaifbarber.nl',      'Amsterdam'),
('Kapsalon Diamanten Schaar',  'kapper',           'info@diamantenschaar.nl',      'Rotterdam'),
('Unlock The Meal',            'restaurant',       'hello@unlockthemeal.nl',       'Utrecht'),
('Yalla Alles Reinigen',       'schoonmaak',       'info@yallareinigen.nl',        'Amsterdam'),
('Zorg Connected',             'zorg',             'admin@zorgconnected.nl',       'Den Haag'),
('Bloemen Paradijs',           'bloemenwinkel',    'contact@bloemenparadijs.nl',   'Rotterdam'),
('FitZone Amsterdam',          'sportschool',      'info@fitzoneamsterdam.nl',     'Amsterdam'),
('TechFix Repairs',            'reparatie',        'service@techfixrepairs.nl',    'Utrecht'),
('Tandarts De Haan',           'tandartspraktijk', 'praktijk@tandartsdehaaan.nl',  'Den Haag');

-- =============================================================
-- CONTRACTEN
-- =============================================================
INSERT INTO contracts (client_id, start_date, end_date, monthly_fee, hourly_rate, hours_included, status) VALUES
(1,  '2025-01-01', '2026-01-01', 100.00, 50.00, 24, 'active'),
(2,  '2025-01-15', '2026-01-15',  75.00, 50.00, 18, 'active'),
(3,  '2025-02-01', '2026-02-01',  75.00, 50.00, 18, 'active'),
(4,  '2025-03-01', '2026-03-01',  75.00, 50.00, 18, 'active'),
(5,  '2025-01-01', '2026-01-01',  50.00, 50.00, 12, 'active'),
(6,  '2025-06-01', '2026-06-01', 150.00, 50.00, 36, 'active'),
(7,  '2025-02-15', '2026-02-15',  75.00, 50.00, 18, 'active'),
(8,  '2025-01-01', '2026-01-01', 100.00, 50.00, 24, 'active'),
(9,  '2024-01-01', '2025-01-01',  50.00, 50.00, 12, 'expired'),
(10, '2025-03-01', '2026-03-01',  75.00, 50.00, 18, 'active');

-- =============================================================
-- ONDERHOUD TAKEN
-- =============================================================
INSERT INTO maintenance_tasks (contract_id, description, category, hours_spent, performed_date) VALUES

-- Lela Restaurant (contract 1) — 26u van 24u → 2u overschreden
(1, 'Volledig redesign menu pagina met fotos en categorieen',         'feature',  6.0, '2025-02-01'),
(1, 'Online reserveringssysteem koppelen aan Google Calendar',        'feature',  8.0, '2025-03-10'),
(1, 'Openingstijden aanpassen voor Ramadan periode',                  'content',  1.0, '2025-03-15'),
(1, 'Google Reviews widget integreren op homepage',                   'feature',  3.0, '2025-04-20'),
(1, 'Afbeeldingsgalerij optimaliseren voor mobiele weergave',         'update',   2.0, '2025-05-01'),
(1, 'Contactpagina bijwerken: nieuw adres en telefoonnummer',         'content',  0.5, '2025-05-15'),
(1, 'Bug: reserveringssysteem genereerde dubbele boekingen',          'bug_fix',  4.0, '2025-06-01'),
(1, 'Zomermenu 2025 verwerken inclusief allergeneninformatie',        'content',  1.5, '2025-06-15'),

-- AlSaif Barber (contract 2) — 14.5u van 18u → 3.5u resterend
(2, 'Prijslijst pagina: kapsels, baardverzorging en tarieven',        'content',  2.0, '2025-02-01'),
(2, 'Live Instagram feed integratie op homepage',                     'feature',  3.0, '2025-03-01'),
(2, 'Online afspraken systeem via Calendly integratie',               'feature',  5.0, '2025-04-10'),
(2, 'Fotogalerij bijwerken met nieuwe werken (lente-update)',         'content',  1.5, '2025-05-05'),
(2, 'Beveiligingsupdate kritieke WordPress plugins',                  'security', 1.0, '2025-05-20'),
(2, 'Trage laadtijd homepagina oplossen (afbeeldingen comprimeren)',  'bug_fix',  2.0, '2025-06-10'),

-- Kapsalon Diamanten Schaar (contract 3) — 15.5u van 18u → 2.5u resterend
(3, 'Volledige website redesign nieuwe huisstijl',                    'feature',  8.0, '2025-03-01'),
(3, 'Diensten en behandelingen pagina uitbreiden met prijzen',        'content',  2.0, '2025-04-01'),
(3, 'Digitaal cadeaubon bestel- en inleveringssysteem',              'feature',  4.0, '2025-04-20'),
(3, 'Mobiele weergave hamburgermenu fixen na update',                 'bug_fix',  1.5, '2025-05-10'),

-- Unlock The Meal (contract 4) — 13u van 18u → 5u resterend
(4, 'Interactieve bezorggebied kaart op basis van postcode',          'feature',  4.0, '2025-04-01'),
(4, 'Menu uitbreiden met allergenen, calorieen en filters',           'content',  3.0, '2025-04-20'),
(4, 'iDeal en PayPal betaalintegratie voor online bestellingen',      'feature',  5.0, '2025-05-10'),
(4, 'Seizoensactie promotiebanner homepagina aanmaken',               'content',  1.0, '2025-05-25'),

-- Yalla Alles Reinigen (contract 5) — 12u van 12u → EXACT op
(5, 'Diensten pagina: beschrijvingen, fotos en tarieven',             'content',  3.0, '2025-02-01'),
(5, 'Online offerte aanvraagformulier met e-mailnotificatie',         'feature',  4.0, '2025-03-15'),
(5, 'Contactgegevens, werkgebied en openingstijden update',           'content',  0.5, '2025-04-01'),
(5, 'Google My Business koppeling en lokale schema markup',           'feature',  3.0, '2025-04-20'),
(5, 'Plugin update na beveiligingsstoring e-mailformulier',           'bug_fix',  1.5, '2025-05-10'),

-- Zorg Connected (contract 6) — 38u van 36u → 2u overschreden
(6, 'Patientenportaal v2.0 bouwen met beveiligd inlogsysteem',        'feature', 12.0, '2025-07-01'),
(6, 'AVG/GDPR updates 2025 verwerken en cookie consent herbouwen',    'security', 3.0, '2025-08-01'),
(6, 'Zorgindicatoren dashboard: grafieken en filteropties',           'feature',  8.0, '2025-09-15'),
(6, 'Nieuwsbrief Q4 campagne aanmaken via Mailchimp',                 'content',  2.0, '2025-10-01'),
(6, 'Kritieke bug: portaal inlog werkte niet na PHP 8.3 update',      'bug_fix',  3.0, '2025-11-01'),
(6, 'Jaarlijkse content review zorgpaginas Q4 2025',                  'content',  2.0, '2025-11-20'),
(6, 'Multi-factor authenticatie (MFA) toevoegen aan portaal',         'security', 4.0, '2025-12-10'),
(6, 'WCAG 2.2 toegankelijkheidsverbeteringen fase 2',                 'update',   4.0, '2026-01-15'),

-- Bloemen Paradijs (contract 7) — 12.5u van 18u → 5.5u resterend
(7, 'Webshop bouwen voor boeketten, planten en bloemstukken',         'feature',  6.0, '2025-03-01'),
(7, 'Bezorgkosten calculator op basis van postcode',                  'feature',  3.0, '2025-04-01'),
(7, 'Seizoensaanbod lente-update met nieuwe productfotos',            'content',  1.5, '2025-04-20'),
(7, 'SSL-storing opgelost op betalingspagina webshop',                'bug_fix',  2.0, '2025-05-01'),

-- FitZone Amsterdam (contract 8) — 30u van 24u → 6u overschreden
(8, 'Online lidmaatschap registratie en geintegreerd betaalsysteem',  'feature',  8.0, '2025-02-01'),
(8, 'Dynamisch lesrooster met filteren op trainer en discipline',     'feature',  6.0, '2025-03-15'),
(8, 'Trainer profielen pagina met fotos en specialisaties',           'feature',  3.0, '2025-04-01'),
(8, 'Fotos en promotievideo sectie homepage toevoegen',               'content',  2.0, '2025-04-20'),
(8, 'Ledenportaal: inlogproblemen na WordPress core update',          'bug_fix',  4.0, '2025-05-10'),
(8, 'Maandelijkse nieuwsbrief template ontwerpen in Mailchimp',       'content',  1.5, '2025-05-25'),
(8, 'Security patches drie kritieke plugins (CVSS score > 8)',        'security', 2.0, '2025-06-01'),
(8, 'Performance optimalisatie: server-side caching en WebP images',  'update',   3.5, '2025-06-10'),

-- TechFix Repairs (contract 9, VERLOPEN) — 7.5u van 12u
(9, 'Diensten en prijzen pagina opmaken',                             'content',  2.0, '2024-02-01'),
(9, 'Reparatie aanvraagformulier met bevestigings-email bouwen',      'feature',  3.0, '2024-04-15'),
(9, 'Google Reviews widget integreren',                               'feature',  2.0, '2024-06-01'),
(9, 'Openingstijden en vakantiesluiting bijwerken',                   'content',  0.5, '2024-09-01'),

-- Tandarts De Haan (contract 10) — 11u van 18u → 7u resterend
(10, 'Praktijkinformatie en team-pagina met fotos en bios',           'feature',  4.0, '2025-04-01'),
(10, 'Online afsprakensysteem integreren via Docplanner API',         'feature',  5.0, '2025-05-01'),
(10, 'GDPR cookie consent banner implementeren conform AVG',          'security', 2.0, '2025-05-20');

-- =============================================================
-- FACTUREN
-- =============================================================
INSERT INTO invoices (contract_id, invoice_date, extra_hours, amount, status) VALUES
(1, '2025-06-30', 2.0, 100.00, 'paid'),
(8, '2025-06-30', 6.0, 300.00, 'paid'),
(6, '2026-01-31', 2.0, 100.00, 'overdue');
