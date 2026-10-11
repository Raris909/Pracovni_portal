BEGIN;

CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE SCHEMA portal;
SET LOCAL search_path = portal, public;

CREATE TABLE roles (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name text NOT NULL UNIQUE CHECK (length(btrim(name)) > 0),
    description text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE users (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    role_id uuid NOT NULL REFERENCES roles (id),
    email text NOT NULL CHECK (email = btrim(email) AND position('@' IN email) > 1),
    password_hash text NOT NULL CHECK (length(password_hash) > 0),
    first_name text NOT NULL CHECK (length(btrim(first_name)) > 0),
    last_name text NOT NULL CHECK (length(btrim(last_name)) > 0),
    phone text,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX users_email_unique ON users (lower(email));

CREATE TABLE candidate_profiles (
    user_id uuid PRIMARY KEY REFERENCES users (id),
    headline text,
    summary text,
    city text,
    country_code varchar(2) CHECK (country_code ~ '^[A-Z]{2}$'),
    skills text[] NOT NULL DEFAULT '{}',
    current_cv_id uuid,
    current_cv_kind text NOT NULL DEFAULT 'cv' CHECK (current_cv_kind = 'cv'),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE candidate_experiences (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    candidate_id uuid NOT NULL REFERENCES candidate_profiles (user_id) ON DELETE CASCADE,
    employer text NOT NULL CHECK (length(btrim(employer)) > 0),
    position text NOT NULL CHECK (length(btrim(position)) > 0),
    description text,
    start_date date NOT NULL,
    end_date date CHECK (end_date >= start_date)
);
CREATE INDEX candidate_experiences_candidate_idx ON candidate_experiences (candidate_id);

CREATE TABLE candidate_education (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    candidate_id uuid NOT NULL REFERENCES candidate_profiles (user_id) ON DELETE CASCADE,
    school text NOT NULL CHECK (length(btrim(school)) > 0),
    field_of_study text,
    degree text,
    start_date date NOT NULL,
    end_date date CHECK (end_date >= start_date)
);
CREATE INDEX candidate_education_candidate_idx ON candidate_education (candidate_id);

CREATE TABLE companies (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name text NOT NULL CHECK (length(btrim(name)) > 0),
    registration_number text,
    country_code varchar(2) NOT NULL CHECK (country_code ~ '^[A-Z]{2}$'),
    description text,
    website_url text,
    address text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (country_code, registration_number)
);

CREATE TABLE company_members (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    company_id uuid NOT NULL REFERENCES companies (id),
    user_id uuid NOT NULL REFERENCES users (id),
    role text NOT NULL DEFAULT 'recruiter' CHECK (role IN ('owner', 'admin', 'recruiter')),
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (company_id, user_id),
    UNIQUE (id, company_id)
);
CREATE INDEX company_members_user_idx ON company_members (user_id);

-- Soubor je v privátním úložišti. Každé nové nahrání vytváří nový záznam.
CREATE TABLE candidate_documents (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    candidate_id uuid NOT NULL REFERENCES candidate_profiles (user_id),
    kind text NOT NULL CHECK (kind IN ('cv', 'cover_letter', 'other')),
    original_filename text NOT NULL CHECK (length(btrim(original_filename)) > 0),
    storage_key text NOT NULL UNIQUE CHECK (length(btrim(storage_key)) > 0),
    mime_type text NOT NULL CHECK (length(btrim(mime_type)) > 0),
    size_bytes bigint NOT NULL CHECK (size_bytes > 0),
    sha256 varchar(64) NOT NULL CHECK (sha256 ~ '^[0-9a-f]{64}$'),
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (id, candidate_id),
    UNIQUE (id, candidate_id, kind)
);
CREATE INDEX candidate_documents_candidate_idx ON candidate_documents (candidate_id);
ALTER TABLE candidate_profiles ADD CONSTRAINT candidate_profiles_current_cv_fk
    FOREIGN KEY (current_cv_id, user_id, current_cv_kind)
    REFERENCES candidate_documents (id, candidate_id, kind);

CREATE TABLE job_offers (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    company_id uuid NOT NULL REFERENCES companies (id),
    created_by_member_id uuid NOT NULL,
    title text NOT NULL CHECK (length(btrim(title)) > 0),
    description text NOT NULL CHECK (length(btrim(description)) > 0),
    requirements text NOT NULL DEFAULT '',
    location text,
    work_mode text NOT NULL CHECK (work_mode IN ('onsite', 'hybrid', 'remote')),
    employment_type text NOT NULL CHECK (employment_type IN ('full_time', 'part_time', 'contract', 'internship')),
    salary_min numeric(12,2) CHECK (salary_min >= 0),
    salary_max numeric(12,2) CHECK (salary_max >= 0),
    salary_currency varchar(3) CHECK (salary_currency ~ '^[A-Z]{3}$'),
    salary_period text CHECK (salary_period IN ('hour', 'month', 'year')),
    status text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'published', 'closed', 'archived')),
    published_at timestamptz,
    application_deadline timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    FOREIGN KEY (created_by_member_id, company_id) REFERENCES company_members (id, company_id),
    CHECK (salary_max IS NULL OR salary_min IS NULL OR salary_max >= salary_min),
    CHECK ((salary_min IS NULL AND salary_max IS NULL AND salary_currency IS NULL AND salary_period IS NULL)
        OR ((salary_min IS NOT NULL OR salary_max IS NOT NULL) AND salary_currency IS NOT NULL AND salary_period IS NOT NULL)),
    CHECK (status <> 'published' OR published_at IS NOT NULL),
    CHECK (application_deadline IS NULL OR published_at IS NULL OR application_deadline > published_at),
    UNIQUE (id, company_id)
);
CREATE INDEX job_offers_company_idx ON job_offers (company_id, status);
CREATE INDEX job_offers_public_idx ON job_offers (published_at DESC) WHERE status = 'published';
CREATE INDEX job_offers_search_idx ON job_offers USING gin (
    to_tsvector('simple', title || ' ' || description || ' ' || requirements)
);

CREATE TABLE recruitment_stages (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    job_offer_id uuid NOT NULL REFERENCES job_offers (id),
    name text NOT NULL CHECK (length(btrim(name)) > 0),
    kind text NOT NULL CHECK (kind IN ('screening', 'interview', 'test', 'offer', 'other')),
    position integer NOT NULL CHECK (position > 0),
    description text,
    UNIQUE (job_offer_id, position) DEFERRABLE INITIALLY IMMEDIATE,
    UNIQUE (id, job_offer_id),
    UNIQUE (id, job_offer_id, kind)
);

CREATE TABLE applications (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    job_offer_id uuid NOT NULL,
    company_id uuid NOT NULL,
    candidate_id uuid NOT NULL REFERENCES candidate_profiles (user_id),
    assigned_member_id uuid,
    status text NOT NULL DEFAULT 'submitted'
        CHECK (status IN ('submitted', 'in_progress', 'rejected', 'accepted', 'withdrawn')),
    cover_message text,
    -- Údaje, které uchazeč při odeslání skutečně zpřístupnil firmě.
    profile_snapshot jsonb NOT NULL CHECK (jsonb_typeof(profile_snapshot) = 'object'),
    submitted_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    FOREIGN KEY (job_offer_id, company_id) REFERENCES job_offers (id, company_id),
    FOREIGN KEY (assigned_member_id, company_id) REFERENCES company_members (id, company_id),
    UNIQUE (job_offer_id, candidate_id),
    UNIQUE (id, candidate_id),
    UNIQUE (id, company_id, candidate_id),
    UNIQUE (id, job_offer_id, company_id)
);
CREATE INDEX applications_candidate_idx ON applications (candidate_id, submitted_at DESC);
CREATE INDEX applications_company_idx ON applications (company_id, status);
CREATE INDEX applications_assignee_idx ON applications (assigned_member_id) WHERE assigned_member_id IS NOT NULL;

CREATE TABLE application_documents (
    application_id uuid NOT NULL,
    document_id uuid NOT NULL,
    candidate_id uuid NOT NULL,
    attached_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (application_id, document_id),
    FOREIGN KEY (application_id, candidate_id) REFERENCES applications (id, candidate_id),
    FOREIGN KEY (document_id, candidate_id) REFERENCES candidate_documents (id, candidate_id),
    UNIQUE (application_id, document_id, candidate_id)
);
CREATE INDEX application_documents_document_idx ON application_documents (document_id);

CREATE TABLE application_stages (
    application_id uuid NOT NULL,
    stage_id uuid NOT NULL,
    job_offer_id uuid NOT NULL,
    company_id uuid NOT NULL,
    status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'in_progress', 'passed', 'failed', 'skipped')),
    started_at timestamptz,
    completed_at timestamptz,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (application_id, stage_id),
    FOREIGN KEY (application_id, job_offer_id, company_id) REFERENCES applications (id, job_offer_id, company_id),
    FOREIGN KEY (stage_id, job_offer_id) REFERENCES recruitment_stages (id, job_offer_id),
    CHECK (completed_at IS NULL OR (started_at IS NOT NULL AND completed_at >= started_at)),
    CHECK ((status IN ('passed', 'failed', 'skipped')) = (completed_at IS NOT NULL)),
    CHECK (status <> 'in_progress' OR started_at IS NOT NULL),
    UNIQUE (application_id, stage_id, company_id)
);
CREATE INDEX application_stages_stage_idx ON application_stages (stage_id);

CREATE TABLE candidate_evaluations (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    application_id uuid NOT NULL,
    stage_id uuid NOT NULL,
    company_id uuid NOT NULL,
    author_member_id uuid NOT NULL,
    score smallint CHECK (score BETWEEN 0 AND 100),
    note text NOT NULL CHECK (length(btrim(note)) > 0),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    FOREIGN KEY (application_id, stage_id, company_id) REFERENCES application_stages (application_id, stage_id, company_id),
    FOREIGN KEY (author_member_id, company_id) REFERENCES company_members (id, company_id)
);
CREATE INDEX candidate_evaluations_stage_idx ON candidate_evaluations (application_id, stage_id);
CREATE INDEX candidate_evaluations_author_idx ON candidate_evaluations (author_member_id);

CREATE TABLE interview_slots (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    job_offer_id uuid NOT NULL,
    company_id uuid NOT NULL,
    stage_id uuid NOT NULL,
    stage_kind text NOT NULL DEFAULT 'interview' CHECK (stage_kind = 'interview'),
    interviewer_member_id uuid NOT NULL,
    starts_at timestamptz NOT NULL,
    ends_at timestamptz NOT NULL CHECK (ends_at > starts_at),
    location text,
    meeting_url text,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    FOREIGN KEY (job_offer_id, company_id) REFERENCES job_offers (id, company_id),
    FOREIGN KEY (stage_id, job_offer_id, stage_kind) REFERENCES recruitment_stages (id, job_offer_id, kind),
    FOREIGN KEY (interviewer_member_id, company_id) REFERENCES company_members (id, company_id),
    UNIQUE (id, job_offer_id, stage_id, company_id),
    EXCLUDE USING gist (interviewer_member_id WITH =, tstzrange(starts_at, ends_at, '[)') WITH &&)
        WHERE (is_active)
);
CREATE INDEX interview_slots_offer_stage_idx ON interview_slots (job_offer_id, stage_id, starts_at);

CREATE TABLE interview_bookings (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    slot_id uuid NOT NULL,
    application_id uuid NOT NULL,
    stage_id uuid NOT NULL,
    job_offer_id uuid NOT NULL,
    company_id uuid NOT NULL,
    status text NOT NULL DEFAULT 'confirmed' CHECK (status IN ('confirmed', 'cancelled', 'completed', 'no_show')),
    booked_at timestamptz NOT NULL DEFAULT now(),
    cancelled_at timestamptz,
    updated_at timestamptz NOT NULL DEFAULT now(),
    FOREIGN KEY (slot_id, job_offer_id, stage_id, company_id) REFERENCES interview_slots (id, job_offer_id, stage_id, company_id),
    FOREIGN KEY (application_id, job_offer_id, company_id) REFERENCES applications (id, job_offer_id, company_id),
    FOREIGN KEY (application_id, stage_id, company_id) REFERENCES application_stages (application_id, stage_id, company_id),
    CHECK ((status = 'cancelled') = (cancelled_at IS NOT NULL)),
    CHECK (cancelled_at IS NULL OR cancelled_at >= booked_at)
);
CREATE UNIQUE INDEX interview_bookings_slot_unique ON interview_bookings (slot_id) WHERE status <> 'cancelled';
CREATE UNIQUE INDEX interview_bookings_application_stage_unique ON interview_bookings (application_id, stage_id) WHERE status <> 'cancelled';
CREATE INDEX interview_bookings_slot_idx ON interview_bookings (slot_id);
CREATE INDEX interview_bookings_application_idx ON interview_bookings (application_id, stage_id);

CREATE TABLE candidate_cv_reviews (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    candidate_id uuid NOT NULL,
    document_id uuid NOT NULL,
    document_kind text NOT NULL DEFAULT 'cv' CHECK (document_kind = 'cv'),
    status text NOT NULL DEFAULT 'queued' CHECK (status IN ('queued', 'processing', 'succeeded', 'failed')),
    provider text NOT NULL CHECK (length(btrim(provider)) > 0),
    model text NOT NULL CHECK (length(btrim(model)) > 0),
    prompt_version text NOT NULL CHECK (length(btrim(prompt_version)) > 0),
    score smallint CHECK (score BETWEEN 0 AND 100),
    result jsonb CHECK (jsonb_typeof(result) = 'object'),
    error_message text,
    created_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz CHECK (completed_at >= created_at),
    updated_at timestamptz NOT NULL DEFAULT now(),
    FOREIGN KEY (document_id, candidate_id, document_kind) REFERENCES candidate_documents (id, candidate_id, kind),
    CHECK ((status IN ('succeeded', 'failed')) = (completed_at IS NOT NULL)),
    CHECK (status <> 'succeeded' OR (score IS NOT NULL AND result IS NOT NULL)),
    CHECK (status <> 'failed' OR error_message IS NOT NULL)
);
CREATE INDEX candidate_cv_reviews_candidate_idx ON candidate_cv_reviews (candidate_id, created_at DESC);
CREATE INDEX candidate_cv_reviews_document_idx ON candidate_cv_reviews (document_id);

CREATE TABLE application_cv_reviews (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    application_id uuid NOT NULL,
    company_id uuid NOT NULL,
    candidate_id uuid NOT NULL,
    document_id uuid NOT NULL,
    document_kind text NOT NULL DEFAULT 'cv' CHECK (document_kind = 'cv'),
    requested_by_member_id uuid NOT NULL,
    status text NOT NULL DEFAULT 'queued' CHECK (status IN ('queued', 'processing', 'succeeded', 'failed')),
    provider text NOT NULL CHECK (length(btrim(provider)) > 0),
    model text NOT NULL CHECK (length(btrim(model)) > 0),
    prompt_version text NOT NULL CHECK (length(btrim(prompt_version)) > 0),
    job_requirements_snapshot jsonb NOT NULL CHECK (jsonb_typeof(job_requirements_snapshot) = 'object'),
    score smallint CHECK (score BETWEEN 0 AND 100),
    result jsonb CHECK (jsonb_typeof(result) = 'object'),
    error_message text,
    created_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz CHECK (completed_at >= created_at),
    updated_at timestamptz NOT NULL DEFAULT now(),
    FOREIGN KEY (application_id, company_id, candidate_id) REFERENCES applications (id, company_id, candidate_id),
    FOREIGN KEY (application_id, document_id, candidate_id) REFERENCES application_documents (application_id, document_id, candidate_id),
    FOREIGN KEY (document_id, candidate_id, document_kind) REFERENCES candidate_documents (id, candidate_id, kind),
    FOREIGN KEY (requested_by_member_id, company_id) REFERENCES company_members (id, company_id),
    CHECK ((status IN ('succeeded', 'failed')) = (completed_at IS NOT NULL)),
    CHECK (status <> 'succeeded' OR (score IS NOT NULL AND result IS NOT NULL)),
    CHECK (status <> 'failed' OR error_message IS NOT NULL)
);
CREATE INDEX application_cv_reviews_application_idx ON application_cv_reviews (application_id, created_at DESC);
CREATE INDEX application_cv_reviews_document_idx ON application_cv_reviews (document_id);
CREATE INDEX application_cv_reviews_requester_idx ON application_cv_reviews (requested_by_member_id);

CREATE TABLE conversations (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    application_id uuid NOT NULL UNIQUE,
    company_id uuid NOT NULL,
    candidate_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    FOREIGN KEY (application_id, company_id, candidate_id) REFERENCES applications (id, company_id, candidate_id)
);
CREATE INDEX conversations_candidate_idx ON conversations (candidate_id);
CREATE INDEX conversations_company_idx ON conversations (company_id);

CREATE TABLE chat_messages (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id uuid NOT NULL REFERENCES conversations (id),
    sender_user_id uuid NOT NULL REFERENCES users (id),
    body text NOT NULL CHECK (length(btrim(body)) > 0),
    sent_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX chat_messages_conversation_idx ON chat_messages (conversation_id, sent_at, id);
CREATE INDEX chat_messages_sender_idx ON chat_messages (sender_user_id);

CREATE FUNCTION touch_updated_at() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END;
$$;

DO $$
DECLARE table_name text;
BEGIN
    FOREACH table_name IN ARRAY ARRAY[
        'roles', 'users', 'candidate_profiles', 'companies', 'company_members', 'job_offers',
        'applications', 'application_stages', 'candidate_evaluations', 'interview_slots',
        'interview_bookings', 'candidate_cv_reviews', 'application_cv_reviews'
    ] LOOP
        EXECUTE format('CREATE TRIGGER touch_updated_at BEFORE UPDATE ON portal.%I FOR EACH ROW EXECUTE FUNCTION portal.touch_updated_at()', table_name);
    END LOOP;
END;
$$;

CREATE FUNCTION keep_document_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW IS DISTINCT FROM OLD THEN
        RAISE EXCEPTION 'Dokument nelze přepsat; nahrajte novou verzi jako nový záznam.' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER keep_document_immutable BEFORE UPDATE ON candidate_documents
    FOR EACH ROW EXECUTE FUNCTION keep_document_immutable();

CREATE FUNCTION check_chat_sender() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM portal.conversations c
        JOIN portal.users u ON u.id = NEW.sender_user_id AND u.is_active
        WHERE c.id = NEW.conversation_id
          AND (c.candidate_id = u.id OR EXISTS (
              SELECT 1 FROM portal.company_members m
              WHERE m.company_id = c.company_id AND m.user_id = u.id AND m.is_active
          ))
    ) THEN
        RAISE EXCEPTION 'Odesílatel nemá přístup ke konverzaci.' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER check_chat_sender BEFORE INSERT OR UPDATE ON chat_messages
    FOR EACH ROW EXECUTE FUNCTION check_chat_sender();

COMMIT;