--
-- PostgreSQL database dump
--

-- Dumped from database version 13.2
-- Dumped by pg_dump version 13.2

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

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: users; Type: TABLE; Schema: public; Owner: daniel
--

CREATE TABLE public.users (
    first_name character varying(50) NOT NULL,
    last_name character varying(50) NOT NULL,
    email character varying(255) NOT NULL,
    account_id uuid NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.users OWNER TO daniel;

--
-- Data for Name: users; Type: TABLE DATA; Schema: public; Owner: daniel
--

COPY public.users (first_name, last_name, email, account_id, created_at) FROM stdin;
\.


--
-- Name: users users_account_id_key; Type: CONSTRAINT; Schema: public; Owner: daniel
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_account_id_key UNIQUE (account_id);


--
-- Name: users users_email_key; Type: CONSTRAINT; Schema: public; Owner: daniel
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_email_key UNIQUE (email);


--
-- Name: users users_pkey; Type: CONSTRAINT; Schema: public; Owner: daniel
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (email, account_id);


--
-- PostgreSQL database dump complete
--

