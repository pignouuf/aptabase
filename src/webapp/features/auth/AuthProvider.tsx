import { useQuery } from "@tanstack/react-query";
import { createContext, useContext } from "react";
import { Navigate } from "react-router-dom";
import { UserAccount, me } from "./auth";
import { ErrorState } from "@components/ErrorState";
import { LoadingState } from "@components/LoadingState";

type Props = {
  children: React.ReactNode;
};

type AuthContextType = {
  user: UserAccount | undefined;
};

const AuthContext = createContext<AuthContextType>({ user: undefined });

export function AuthProvider(props: Props) {
  const { isLoading, isError, data: user } = useQuery({ queryKey: ["me"], queryFn: me });

  if (isLoading) return <LoadingState size="lg" color="primary" delay={0} />;
  if (isError) return <ErrorState />;
  if (!user) return <Navigate to="/auth" />;

  return <AuthContext.Provider value={{ user }}>{props.children}</AuthContext.Provider>;
}

export function useAuthState(): AuthContextType {
  return useContext(AuthContext);
}

export function useAuth(): UserAccount {
  const ctx = useAuthState();
  if (!ctx.user) {
    throw new Error("User is not authenticated");
  }

  return ctx.user;
}
