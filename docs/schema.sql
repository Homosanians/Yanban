--
-- PostgreSQL database dump
--

-- Dumped from database version 16.4
-- Dumped by pg_dump version 16.4

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: enqueue_object_deletion(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.enqueue_object_deletion() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    INSERT INTO object_deletions (storage_key, enqueued_at, attempts)
    VALUES (OLD.storage_key, now(), 0);
    RETURN OLD;
END;
$$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: __EFMigrationsHistory; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL
);


--
-- Name: activity_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.activity_logs (
    id uuid NOT NULL,
    sequence bigint NOT NULL,
    board_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    action character varying(20) NOT NULL,
    entity_type character varying(20) NOT NULL,
    entity_id uuid NOT NULL,
    summary character varying(500),
    created_at timestamp with time zone NOT NULL,
    new_value character varying(500),
    old_value character varying(500),
    search_vector tsvector GENERATED ALWAYS AS (((setweight(to_tsvector('russian'::regconfig, (COALESCE(summary, ''::character varying))::text), 'A'::"char") || setweight(to_tsvector('russian'::regconfig, (COALESCE(old_value, ''::character varying))::text), 'B'::"char")) || setweight(to_tsvector('russian'::regconfig, (COALESCE(new_value, ''::character varying))::text), 'B'::"char"))) STORED
);


--
-- Name: activity_logs_sequence_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.activity_logs ALTER COLUMN sequence ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.activity_logs_sequence_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: attachments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.attachments (
    id uuid NOT NULL,
    card_id uuid NOT NULL,
    file_name character varying(255) NOT NULL,
    content_type character varying(255) NOT NULL,
    size_bytes bigint NOT NULL,
    storage_key character varying(512) NOT NULL,
    status character varying(16) NOT NULL,
    uploaded_by_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL
);


--
-- Name: board_members; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.board_members (
    board_id uuid NOT NULL,
    user_id uuid NOT NULL,
    role character varying(20) NOT NULL
);


--
-- Name: boards; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.boards (
    id uuid NOT NULL,
    owner_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    archived_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL
);


--
-- Name: card_templates; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.card_templates (
    id uuid NOT NULL,
    board_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    title character varying(500) NOT NULL,
    description text,
    created_by_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL
);


--
-- Name: cards; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cards (
    id uuid NOT NULL,
    list_id uuid NOT NULL,
    title character varying(500) NOT NULL,
    description text,
    rank character varying(64) NOT NULL,
    assignee_id uuid,
    created_by_id uuid NOT NULL,
    due_date timestamp with time zone,
    archived_at timestamp with time zone,
    search_vector tsvector GENERATED ALWAYS AS ((setweight(to_tsvector('russian'::regconfig, (COALESCE(title, ''::character varying))::text), 'A'::"char") || setweight(to_tsvector('russian'::regconfig, COALESCE(description, ''::text)), 'B'::"char"))) STORED
);


--
-- Name: comments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.comments (
    id uuid NOT NULL,
    card_id uuid NOT NULL,
    author_id uuid NOT NULL,
    body character varying(5000) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    edited_at timestamp with time zone
);


--
-- Name: email_confirmation_tokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.email_confirmation_tokens (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    token_hash character varying(64) NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    consumed_at timestamp with time zone
);


--
-- Name: lists; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.lists (
    id uuid NOT NULL,
    board_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    rank character varying(64) NOT NULL
);


--
-- Name: notification_preferences; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.notification_preferences (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    board_id uuid,
    type character varying(30) NOT NULL,
    enabled boolean NOT NULL
);


--
-- Name: object_deletions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.object_deletions (
    id bigint NOT NULL,
    storage_key character varying(512) NOT NULL,
    enqueued_at timestamp with time zone NOT NULL,
    deleted_at timestamp with time zone,
    attempts integer NOT NULL,
    last_error character varying(1000)
);


--
-- Name: object_deletions_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.object_deletions ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.object_deletions_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: outbox_messages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.outbox_messages (
    id uuid NOT NULL,
    type character varying(30) NOT NULL,
    recipient_user_id uuid NOT NULL,
    recipient_email character varying(320) NOT NULL,
    board_id uuid,
    payload jsonb,
    status character varying(10) NOT NULL,
    attempts integer NOT NULL,
    next_attempt_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    sent_at timestamp with time zone,
    last_error character varying(1000)
);


--
-- Name: refresh_tokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.refresh_tokens (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    token_hash character varying(88) NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone,
    replaced_by_token_id uuid
);


--
-- Name: users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.users (
    id uuid NOT NULL,
    email character varying(320) NOT NULL,
    password_hash text NOT NULL,
    display_name character varying(100) NOT NULL,
    token_version integer DEFAULT 0 NOT NULL,
    vk_id bigint,
    created_at timestamp with time zone NOT NULL,
    email_confirmed_at timestamp with time zone
);


--
-- Name: activity_logs ak_activity_logs_sequence; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.activity_logs
    ADD CONSTRAINT ak_activity_logs_sequence UNIQUE (sequence);


--
-- Name: __EFMigrationsHistory pk___ef_migrations_history; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."__EFMigrationsHistory"
    ADD CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id);


--
-- Name: activity_logs pk_activity_logs; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.activity_logs
    ADD CONSTRAINT pk_activity_logs PRIMARY KEY (id);


--
-- Name: attachments pk_attachments; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.attachments
    ADD CONSTRAINT pk_attachments PRIMARY KEY (id);


--
-- Name: board_members pk_board_members; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.board_members
    ADD CONSTRAINT pk_board_members PRIMARY KEY (board_id, user_id);


--
-- Name: boards pk_boards; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.boards
    ADD CONSTRAINT pk_boards PRIMARY KEY (id);


--
-- Name: card_templates pk_card_templates; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.card_templates
    ADD CONSTRAINT pk_card_templates PRIMARY KEY (id);


--
-- Name: cards pk_cards; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cards
    ADD CONSTRAINT pk_cards PRIMARY KEY (id);


--
-- Name: comments pk_comments; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.comments
    ADD CONSTRAINT pk_comments PRIMARY KEY (id);


--
-- Name: email_confirmation_tokens pk_email_confirmation_tokens; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.email_confirmation_tokens
    ADD CONSTRAINT pk_email_confirmation_tokens PRIMARY KEY (id);


--
-- Name: lists pk_lists; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.lists
    ADD CONSTRAINT pk_lists PRIMARY KEY (id);


--
-- Name: notification_preferences pk_notification_preferences; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification_preferences
    ADD CONSTRAINT pk_notification_preferences PRIMARY KEY (id);


--
-- Name: object_deletions pk_object_deletions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.object_deletions
    ADD CONSTRAINT pk_object_deletions PRIMARY KEY (id);


--
-- Name: outbox_messages pk_outbox_messages; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.outbox_messages
    ADD CONSTRAINT pk_outbox_messages PRIMARY KEY (id);


--
-- Name: refresh_tokens pk_refresh_tokens; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT pk_refresh_tokens PRIMARY KEY (id);


--
-- Name: users pk_users; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT pk_users PRIMARY KEY (id);


--
-- Name: ix_activity_logs_board_id_sequence; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_activity_logs_board_id_sequence ON public.activity_logs USING btree (board_id, sequence DESC);


--
-- Name: ix_activity_logs_search_vector; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_activity_logs_search_vector ON public.activity_logs USING gin (search_vector);


--
-- Name: ix_attachments_card_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_attachments_card_id ON public.attachments USING btree (card_id);


--
-- Name: ix_attachments_uploaded_by_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_attachments_uploaded_by_id ON public.attachments USING btree (uploaded_by_id);


--
-- Name: ix_board_members_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_board_members_user_id ON public.board_members USING btree (user_id);


--
-- Name: ix_boards_owner_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_boards_owner_id ON public.boards USING btree (owner_id);


--
-- Name: ix_card_templates_board_id_name; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_card_templates_board_id_name ON public.card_templates USING btree (board_id, name);


--
-- Name: ix_card_templates_created_by_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_card_templates_created_by_id ON public.card_templates USING btree (created_by_id);


--
-- Name: ix_cards_assignee_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cards_assignee_id ON public.cards USING btree (assignee_id);


--
-- Name: ix_cards_created_by_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cards_created_by_id ON public.cards USING btree (created_by_id);


--
-- Name: ix_cards_list_id_rank; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cards_list_id_rank ON public.cards USING btree (list_id, rank);


--
-- Name: ix_cards_search_vector; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cards_search_vector ON public.cards USING gin (search_vector);


--
-- Name: ix_comments_author_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_comments_author_id ON public.comments USING btree (author_id);


--
-- Name: ix_comments_card_id_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_comments_card_id_created_at ON public.comments USING btree (card_id, created_at);


--
-- Name: ix_email_confirmation_tokens_token_hash; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_email_confirmation_tokens_token_hash ON public.email_confirmation_tokens USING btree (token_hash);


--
-- Name: ix_email_confirmation_tokens_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_email_confirmation_tokens_user_id ON public.email_confirmation_tokens USING btree (user_id);


--
-- Name: ix_lists_board_id_rank; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_lists_board_id_rank ON public.lists USING btree (board_id, rank);


--
-- Name: ix_notification_preferences_board_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_notification_preferences_board_id ON public.notification_preferences USING btree (board_id);


--
-- Name: ix_object_deletions_pending; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_object_deletions_pending ON public.object_deletions USING btree (enqueued_at) WHERE (deleted_at IS NULL);


--
-- Name: ix_outbox_messages_pending; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_outbox_messages_pending ON public.outbox_messages USING btree (status, next_attempt_at) WHERE ((status)::text = 'Pending'::text);


--
-- Name: ix_refresh_tokens_token_hash; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_refresh_tokens_token_hash ON public.refresh_tokens USING btree (token_hash);


--
-- Name: ix_refresh_tokens_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_refresh_tokens_user_id ON public.refresh_tokens USING btree (user_id);


--
-- Name: ix_users_email; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_users_email ON public.users USING btree (email);


--
-- Name: ix_users_vk_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_users_vk_id ON public.users USING btree (vk_id);


--
-- Name: ux_notification_preferences_board; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_notification_preferences_board ON public.notification_preferences USING btree (user_id, board_id, type) WHERE (board_id IS NOT NULL);


--
-- Name: ux_notification_preferences_global; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_notification_preferences_global ON public.notification_preferences USING btree (user_id, type) WHERE (board_id IS NULL);


--
-- Name: attachments trg_attachment_deleted; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_attachment_deleted AFTER DELETE ON public.attachments FOR EACH ROW EXECUTE FUNCTION public.enqueue_object_deletion();


--
-- Name: attachments fk_attachments_cards_card_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.attachments
    ADD CONSTRAINT fk_attachments_cards_card_id FOREIGN KEY (card_id) REFERENCES public.cards(id) ON DELETE CASCADE;


--
-- Name: attachments fk_attachments_users_uploaded_by_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.attachments
    ADD CONSTRAINT fk_attachments_users_uploaded_by_id FOREIGN KEY (uploaded_by_id) REFERENCES public.users(id) ON DELETE RESTRICT;


--
-- Name: board_members fk_board_members_boards_board_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.board_members
    ADD CONSTRAINT fk_board_members_boards_board_id FOREIGN KEY (board_id) REFERENCES public.boards(id) ON DELETE CASCADE;


--
-- Name: board_members fk_board_members_users_user_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.board_members
    ADD CONSTRAINT fk_board_members_users_user_id FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: boards fk_boards_users_owner_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.boards
    ADD CONSTRAINT fk_boards_users_owner_id FOREIGN KEY (owner_id) REFERENCES public.users(id) ON DELETE RESTRICT;


--
-- Name: card_templates fk_card_templates_boards_board_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.card_templates
    ADD CONSTRAINT fk_card_templates_boards_board_id FOREIGN KEY (board_id) REFERENCES public.boards(id) ON DELETE CASCADE;


--
-- Name: card_templates fk_card_templates_users_created_by_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.card_templates
    ADD CONSTRAINT fk_card_templates_users_created_by_id FOREIGN KEY (created_by_id) REFERENCES public.users(id) ON DELETE RESTRICT;


--
-- Name: cards fk_cards_lists_list_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cards
    ADD CONSTRAINT fk_cards_lists_list_id FOREIGN KEY (list_id) REFERENCES public.lists(id) ON DELETE CASCADE;


--
-- Name: cards fk_cards_users_assignee_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cards
    ADD CONSTRAINT fk_cards_users_assignee_id FOREIGN KEY (assignee_id) REFERENCES public.users(id) ON DELETE SET NULL;


--
-- Name: cards fk_cards_users_created_by_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cards
    ADD CONSTRAINT fk_cards_users_created_by_id FOREIGN KEY (created_by_id) REFERENCES public.users(id) ON DELETE RESTRICT;


--
-- Name: comments fk_comments_cards_card_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.comments
    ADD CONSTRAINT fk_comments_cards_card_id FOREIGN KEY (card_id) REFERENCES public.cards(id) ON DELETE CASCADE;


--
-- Name: comments fk_comments_users_author_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.comments
    ADD CONSTRAINT fk_comments_users_author_id FOREIGN KEY (author_id) REFERENCES public.users(id) ON DELETE RESTRICT;


--
-- Name: email_confirmation_tokens fk_email_confirmation_tokens_users_user_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.email_confirmation_tokens
    ADD CONSTRAINT fk_email_confirmation_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: lists fk_lists_boards_board_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.lists
    ADD CONSTRAINT fk_lists_boards_board_id FOREIGN KEY (board_id) REFERENCES public.boards(id) ON DELETE CASCADE;


--
-- Name: notification_preferences fk_notification_preferences_boards_board_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification_preferences
    ADD CONSTRAINT fk_notification_preferences_boards_board_id FOREIGN KEY (board_id) REFERENCES public.boards(id) ON DELETE CASCADE;


--
-- Name: notification_preferences fk_notification_preferences_users_user_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification_preferences
    ADD CONSTRAINT fk_notification_preferences_users_user_id FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: refresh_tokens fk_refresh_tokens_users_user_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT fk_refresh_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

