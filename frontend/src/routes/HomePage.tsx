import { useAuth } from "../auth/useAuth";

export function HomePage() {
  const { user, logout } = useAuth();
  return (
    <div className="home">
      <header>
        <h1>Yanban</h1>
        <div className="user">
          <span>{user?.displayName} ({user?.email})</span>
          <button onClick={() => logout()}>Log out</button>
        </div>
      </header>
      <main>
        <p>You are signed in. Boards arrive in the next milestone.</p>
      </main>
    </div>
  );
}
