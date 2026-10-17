INSERT INTO portal.roles (name, description)
VALUES ('candidate', 'Uchazeč o zaměstnání'), ('company', 'Uživatel firmy')
ON CONFLICT (name) DO NOTHING;
